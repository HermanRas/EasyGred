namespace EasyGred.Core;

public sealed class Layer
{
    public string Name { get; set; }
    public bool Visible { get; set; } = true;
    public uint[] Pixels { get; set; }

    public Layer(string name, int width, int height, uint fill = Px.Transparent)
    {
        Name = name;
        Pixels = new uint[width * height];
        if (fill != 0) Array.Fill(Pixels, fill);
    }

    public Layer(string name, uint[] pixels)
    {
        Name = name;
        Pixels = pixels;
    }

    public Layer Clone() => new(Name, (uint[])Pixels.Clone()) { Visible = Visible };
}

public sealed record DocumentState(int Width, int Height, List<Layer> Layers, int ActiveIndex);

public sealed class Document
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Index 0 is the bottom layer.</summary>
    public List<Layer> Layers { get; } = new();
    public int ActiveIndex { get; set; }
    public Layer Active => Layers[ActiveIndex];

    public string? FilePath { get; set; }
    public bool Dirty { get; set; }

    public Document(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static Document CreateBlank(int width, int height, uint background)
    {
        var doc = new Document(width, height);
        doc.Layers.Add(new Layer("Background", width, height, background));
        return doc;
    }

    public static Document FromPixels(int width, int height, uint[] pixels)
    {
        var doc = new Document(width, height);
        doc.Layers.Add(new Layer("Background", pixels));
        return doc;
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public DocumentState Snapshot() =>
        new(Width, Height, Layers.Select(l => l.Clone()).ToList(), ActiveIndex);

    public void Restore(DocumentState s)
    {
        Width = s.Width;
        Height = s.Height;
        Layers.Clear();
        Layers.AddRange(s.Layers.Select(l => l.Clone()));
        ActiveIndex = Math.Clamp(s.ActiveIndex, 0, Layers.Count - 1);
    }

    /// <summary>Replaces every layer's pixels after a size-changing operation (crop, rotate, resize).</summary>
    public void Transform(int newWidth, int newHeight, Func<uint[], uint[]> transform) =>
        Transform(newWidth, newHeight, (_, px) => transform(px));

    /// <summary>As above, with the layer index (0 = bottom) passed to the transform.</summary>
    public void Transform(int newWidth, int newHeight, Func<int, uint[], uint[]> transform)
    {
        for (int i = 0; i < Layers.Count; i++) Layers[i].Pixels = transform(i, Layers[i].Pixels);
        Width = newWidth;
        Height = newHeight;
    }

    /// <summary>Merges all visible layers into one straight-alpha image (used for saving).</summary>
    public uint[] Flatten()
    {
        var visible = Layers.Where(l => l.Visible).ToList();
        if (visible.Count == 1) return (uint[])visible[0].Pixels.Clone();
        var result = new uint[Width * Height];
        foreach (var layer in visible)
        {
            var src = layer.Pixels;
            for (int i = 0; i < result.Length; i++) result[i] = Px.Over(result[i], src[i]);
        }
        return result;
    }

    /// <summary>
    /// Composites visible layers into a premultiplied buffer for display. A floating selection is
    /// drawn just above the active layer.
    /// </summary>
    public void CompositePremul(uint[] dst, Selection? selection)
    {
        Array.Clear(dst);
        for (int li = 0; li < Layers.Count; li++)
        {
            var layer = Layers[li];
            if (layer.Visible)
            {
                var src = layer.Pixels;
                for (int i = 0; i < dst.Length; i++)
                {
                    uint s = src[i];
                    uint a = s >> 24;
                    if (a == 0) continue;
                    dst[i] = a == 255 ? s : Px.OverPremul(dst[i], Px.Premultiply(s));
                }
            }
            if (li == ActiveIndex && selection?.Floating is { } fl)
                selection.ForEachFloatingPixel(Width, Height, (di, c) => dst[di] = Px.OverPremul(dst[di], Px.Premultiply(c)));
        }
    }

    public void AddLayer(string name)
    {
        Layers.Insert(ActiveIndex + 1, new Layer(name, Width, Height));
        ActiveIndex++;
    }

    public void DuplicateLayer()
    {
        var copy = Active.Clone();
        copy.Name = Active.Name + " copy";
        Layers.Insert(ActiveIndex + 1, copy);
        ActiveIndex++;
    }

    public void DeleteLayer()
    {
        if (Layers.Count <= 1) return;
        Layers.RemoveAt(ActiveIndex);
        ActiveIndex = Math.Clamp(ActiveIndex - 1, 0, Layers.Count - 1);
    }

    public void MoveLayer(int delta)
    {
        int target = ActiveIndex + delta;
        if (target < 0 || target >= Layers.Count) return;
        (Layers[ActiveIndex], Layers[target]) = (Layers[target], Layers[ActiveIndex]);
        ActiveIndex = target;
    }

    public void MergeDown()
    {
        if (ActiveIndex == 0) return;
        var top = Active.Pixels;
        var below = Layers[ActiveIndex - 1].Pixels;
        if (Active.Visible)
            for (int i = 0; i < below.Length; i++) below[i] = Px.Over(below[i], top[i]);
        Layers.RemoveAt(ActiveIndex);
        ActiveIndex--;
    }

    public void FlattenLayers()
    {
        var flat = Flatten();
        Layers.Clear();
        Layers.Add(new Layer("Background", flat));
        ActiveIndex = 0;
    }
}

/// <summary>Snapshot-based undo/redo, bounded by entry count and memory.</summary>
public sealed class History
{
    private const int MaxEntries = 50;
    private const long MaxBytes = 400L * 1024 * 1024;

    private readonly List<DocumentState> _undo = new();
    private readonly List<DocumentState> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(Document doc)
    {
        _undo.Add(doc.Snapshot());
        _redo.Clear();
        doc.Dirty = true;
        while (_undo.Count > 1 && (_undo.Count > MaxEntries || TotalBytes() > MaxBytes)) _undo.RemoveAt(0);
    }

    public bool Undo(Document doc) => Swap(doc, _undo, _redo);
    public bool Redo(Document doc) => Swap(doc, _redo, _undo);

    /// <summary>Drops the most recent undo entry without restoring it (for operations that changed nothing).</summary>
    public void DiscardLast()
    {
        if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static bool Swap(Document doc, List<DocumentState> from, List<DocumentState> to)
    {
        if (from.Count == 0) return false;
        to.Add(doc.Snapshot());
        doc.Restore(from[^1]);
        from.RemoveAt(from.Count - 1);
        doc.Dirty = true;
        return true;
    }

    private long TotalBytes() => _undo.Sum(s => (long)s.Width * s.Height * 4 * s.Layers.Count);
}
