# Build/test image for EasyGred: .NET 8 SDK + the native libs Skia needs at test time,
# plus Xvfb/ImageMagick for headless GUI screenshots.
FROM mcr.microsoft.com/dotnet/sdk:8.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core xvfb xauth x11-utils imagemagick xdotool zip \
    libice6 libsm6 libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 \
 && rm -rf /var/lib/apt/lists/*
