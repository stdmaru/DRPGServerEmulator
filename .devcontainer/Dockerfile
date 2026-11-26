# Dev image: .NET 10 SDK + what NativeAOT needs to link (clang, lld, zlib) for the launcher/client,
# and Python 3 + Pillow for the tools in work/tools and the screenshot checks.
FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends clang lld zlib1g-dev python3 python3-pil \
    && rm -rf /var/lib/apt/lists/*
