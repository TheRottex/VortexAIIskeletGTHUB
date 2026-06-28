#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet restore "$ROOT/VortexAI.sln"
dotnet publish "$ROOT/Vortex.Desktop/Vortex.Desktop.csproj" -c Release -r linux-x64 --self-contained true -o "$ROOT/artifacts/linux-x64/vortex-desktop"
dotnet publish "$ROOT/Vortex.LocalAgent/Vortex.LocalAgent.csproj" -c Release -r linux-x64 --self-contained true -o "$ROOT/artifacts/linux-x64/vortex-local-agent"
dotnet publish "$ROOT/Vortex.Server/Vortex.Server.csproj" -c Release -r linux-x64 --self-contained true -o "$ROOT/artifacts/linux-x64/vortex-server"
