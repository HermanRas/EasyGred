using EasyGred.Core;

namespace EasyGred.Controls;

public enum ImageTransform { FlipH, FlipV, RotateRight, RotateLeft, Rotate180 }

/// <summary>Editing commands invoked from menus and shortcuts.</summary>
public sealed partial class CanvasView
{
    public void LoadDocument(Document doc)
    {
        Doc = doc;
        Selection = null;
        History.Clear();
        _bitmap = null;
        Refresh(resized: true);
    }

    /// <summary>
    /// Writes a floating selection back to the layer. With <paramref name="keepShape"/> the (now
    /// non-floating) selection stays so it can scope the next operation; otherwise it is dropped.
    /// </summary>
    public void CommitSelection(bool keepShape = false)
    {
        if (Selection == null) return;
        Selection.Commit(Layer, Doc.Width, Doc.Height, Settings.TransparentPaste);
        if (!keepShape) Selection = null;
        Refresh();
    }

    public void SelectAll()
    {
        CommitSelection();
        Selection = Selection.FromRect(0, 0, Doc.Width - 1, Doc.Height - 1, Doc.Width, Doc.Height);
        Tool = Tool.RectSelect;
        Refresh();
    }

    public void DeleteSelection()
    {
        if (Selection == null) return;
        if (!Selection.IsFloating)
        {
            History.Push(Doc);
            Selection.Clear(Layer, Doc.Width, Doc.Height);
        }
        Selection = null;
        Refresh();
    }

    /// <summary>The selected pixels, or the whole active layer when nothing is selected.</summary>
    public (int W, int H, uint[] Pixels) CopySelection() =>
        Selection == null
            ? (Doc.Width, Doc.Height, (uint[])Layer.Clone())
            : (Selection.W, Selection.H, Selection.Extract(Layer, Doc.Width, Doc.Height));

    /// <summary>Pastes as a floating selection at (x,y); grows the canvas if the image is bigger.</summary>
    public void Paste(int w, int h, uint[] pixels, int x, int y)
    {
        CommitSelection();
        History.Push(Doc);
        if (w > Doc.Width || h > Doc.Height)
        {
            int ow = Doc.Width, oh = Doc.Height, nw = Math.Max(ow, w), nh = Math.Max(oh, h);
            Doc.Transform(nw, nh, (i, px) => Raster.Crop(px, ow, oh, 0, 0, nw, nh, i == 0 ? Secondary : Px.Transparent));
            x = y = 0;
        }
        x = Math.Clamp(x, 0, Math.Max(0, Doc.Width - w));
        y = Math.Clamp(y, 0, Math.Max(0, Doc.Height - h));
        Selection = Selection.FromPixels(x, y, w, h, pixels);
        Tool = Tool.RectSelect;
        Refresh(resized: true);
    }

    public void Undo() => Step(History.Undo);
    public void Redo() => Step(History.Redo);

    private void Step(Func<Document, bool> op)
    {
        _down = false;
        _selDrag = SelDrag.None;
        Selection = null;
        if (op(Doc)) Refresh(resized: true);
    }

    public void CropToSelection()
    {
        if (Selection == null) return;
        int x = Math.Max(0, Selection.X), y = Math.Max(0, Selection.Y);
        int w = Math.Min(Doc.Width, Selection.X + Selection.W) - x, h = Math.Min(Doc.Height, Selection.Y + Selection.H) - y;
        if (!Selection.IsFloating) History.Push(Doc);
        CommitSelection();
        if (w < 1 || h < 1) return;
        int ow = Doc.Width, oh = Doc.Height;
        Doc.Transform(w, h, px => Raster.Crop(px, ow, oh, x, y, w, h));
        Refresh(resized: true);
    }

    private static (uint[], int, int) Apply(ImageTransform t, uint[] px, int w, int h) => t switch
    {
        ImageTransform.FlipH => (Raster.FlipH(px, w, h), w, h),
        ImageTransform.FlipV => (Raster.FlipV(px, w, h), w, h),
        ImageTransform.RotateRight => (Raster.Rotate(px, w, h, 1), h, w),
        ImageTransform.RotateLeft => (Raster.Rotate(px, w, h, 3), h, w),
        _ => (Raster.Rotate(px, w, h, 2), w, h),
    };

    /// <summary>Flips/rotates the selection if there is one, otherwise the whole image (all layers).</summary>
    public void ApplyTransform(ImageTransform t)
    {
        if (Selection != null)
        {
            if (!Selection.IsFloating)
            {
                History.Push(Doc);
                Selection.Lift(Layer, Doc.Width, Doc.Height);
            }
            Selection.TransformFloating((px, w, h) => Apply(t, px, w, h));
            Refresh();
            return;
        }
        History.Push(Doc);
        int ow = Doc.Width, oh = Doc.Height;
        var (nw, nh) = t is ImageTransform.RotateLeft or ImageTransform.RotateRight ? (oh, ow) : (ow, oh);
        Doc.Transform(nw, nh, px => Apply(t, px, ow, oh).Item1);
        Refresh(resized: true);
    }

    /// <summary>Changes the canvas size; new area on the bottom layer gets the secondary color.</summary>
    public void ResizeCanvas(int nw, int nh, bool centre)
    {
        CommitSelection();
        History.Push(Doc);
        int ow = Doc.Width, oh = Doc.Height;
        int ox = centre ? (nw - ow) / 2 : 0, oy = centre ? (nh - oh) / 2 : 0;
        Doc.Transform(nw, nh, (i, px) => Raster.Crop(px, ow, oh, -ox, -oy, nw, nh, i == 0 ? Secondary : Px.Transparent));
        Refresh(resized: true);
    }

    public void ScaleImage(int nw, int nh, bool smooth)
    {
        CommitSelection();
        History.Push(Doc);
        int ow = Doc.Width, oh = Doc.Height;
        Doc.Transform(nw, nh, px => smooth ? Raster.ResizeSmooth(px, ow, oh, nw, nh) : Raster.ResizeNearest(px, ow, oh, nw, nh));
        Refresh(resized: true);
    }

    /// <summary>Bulk replace; limited to the selection when there is one. Returns pixels changed.</summary>
    public int ReplaceColor(uint from, uint to, int tolerance, bool allLayers)
    {
        CommitSelection(keepShape: true);
        History.Push(Doc);
        var scope = Selection?.ScopeFor(Doc.Width);
        int count = allLayers
            ? Doc.Layers.Sum(l => Raster.ReplaceColor(l.Pixels, from, to, tolerance, scope))
            : Raster.ReplaceColor(Layer, from, to, tolerance, scope);
        if (count == 0) History.DiscardLast();
        Refresh();
        return count;
    }

    public void ClearImage()
    {
        CommitSelection();
        History.Push(Doc);
        Array.Fill(Layer, Doc.ActiveIndex == 0 ? Secondary : Px.Transparent);
        Refresh();
    }

    public void CommitText(int x, int y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        History.Push(Doc);
        var (w, h, px) = TextRenderer.Render(text, Settings.TextStyle, Primary);
        if (Settings.TextOpaque) Raster.FillRect(Layer, Doc.Width, Doc.Height, x, y, x + w - 1, y + h - 1, Secondary);
        Raster.BlendOnto(Layer, Doc.Width, Doc.Height, px, w, h, x, y);
        Refresh();
    }

    /// <summary>Runs a layer operation (add, delete, move, merge...) as one undo step.</summary>
    public void LayerOp(Action<Document> op)
    {
        CommitSelection();
        History.Push(Doc);
        op(Doc);
        Refresh();
    }

    public void SelectLayer(int index)
    {
        if (index < 0 || index >= Doc.Layers.Count || index == Doc.ActiveIndex) return;
        CommitSelection();
        Doc.ActiveIndex = index;
        Refresh();
    }

    public void SetLayerVisible(int index, bool visible)
    {
        Doc.Layers[index].Visible = visible;
        Doc.Dirty = true;
        Refresh();
    }

    public void SwapColors() => (Primary, Secondary) = (Secondary, Primary);
}
