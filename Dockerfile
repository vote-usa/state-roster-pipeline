# syntax=docker/dockerfile:1.7-labs

# Build stage: restore is split from the source copy so package restores cache
# until a csproj changes.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY --parents src/*/*.csproj ./
RUN dotnet restore src/StateBallot.Cli/StateBallot.Cli.csproj

COPY src/ src/
RUN dotnet publish src/StateBallot.Cli/StateBallot.Cli.csproj \
    --configuration Release --no-restore --output /out

# Runtime stage. The CLI locates data/ by walking up from the executable to a
# directory that has src/ and data/ side by side, so the image keeps the repo
# shape: /app/src/StateBallot.Cli (binaries) and /app/data.
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY --from=build /out/ src/StateBallot.Cli/
COPY data/input/ data/input/
COPY db/migrations/ db/migrations/
RUN mkdir -p data/output && chown -R app:app /app

USER app
WORKDIR /app/src/StateBallot.Cli
ENTRYPOINT ["dotnet", "StateBallot.Cli.dll"]
CMD ["--help"]
