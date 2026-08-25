<#
.SYNOPSIS
    Rebuilds the two Plenipo SPAs this product serves and vendors them into wwwroot.

.DESCRIPTION
    Auditworthy has no frontend source of its own. The workspace shell (@plenipo/ui) and the admin
    console (@plenipo/admin-ui) are PLATFORM packages, and the platform host serves them as static
    files if — and only if — their built output is physically present:

        src/Auditworthy.Host/wwwroot/app     <- @plenipo/ui        (mounted at /)
        src/Auditworthy.Host/wwwroot/admin   <- @plenipo/admin-ui  (mounted at /admin)

    Those built assets are COMMITTED to this repo on purpose. It is the same "no registry"
    reasoning that puts Plenipo's nupkgs in .packages/: a fresh clone, and CI, must be able to serve
    the product without a pnpm install against a registry that does not host these packages. This
    script is how you regenerate them when the platform is upgraded — it is not part of the normal
    build, and `dotnet build` never invokes it.

    Two build-time settings are load-bearing:

      VITE_API_BASE=""      Same-origin. @plenipo/client's normalizeApiBase only falls back to its
                            http://localhost:8080 dev default when the value is NULLISH, so the
                            empty string is meaningful and is NOT the same as leaving it unset.
                            Unset it and every request from the served page goes to :8080 and is
                            refused — the shell renders, then sits dead with no data.
      VITE_BRAND_NAME       Baked into <title> and the shell chrome at build time.

.PARAMETER PlatformPath
    Path to a Plenipo platform checkout containing frontend/. Defaults to ../plenipo relative to
    this repo, then $env:PLENIPO_PATH.

.EXAMPLE
    pwsh scripts/build-ui.ps1
    pwsh scripts/build-ui.ps1 -PlatformPath C:\src\plenipo
#>
[CmdletBinding()]
param(
    [string]$PlatformPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$brand = 'Auditworthy'

if (-not $PlatformPath) {
    $candidates = @(
        (Join-Path (Split-Path -Parent $repoRoot) 'plenipo'),
        $env:PLENIPO_PATH
    ) | Where-Object { $_ }
    $PlatformPath = $candidates | Where-Object { Test-Path (Join-Path $_ 'frontend') } | Select-Object -First 1
}

if (-not $PlatformPath -or -not (Test-Path (Join-Path $PlatformPath 'frontend'))) {
    throw "No Plenipo checkout with a frontend/ directory found. Pass -PlatformPath, or set PLENIPO_PATH."
}

$frontend = Join-Path $PlatformPath 'frontend'
Write-Host "Platform frontend: $frontend"

foreach ($tool in 'node', 'pnpm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required to build the UI but was not found on PATH."
    }
}

# Same-origin: the API host serves these files, so a relative base is both correct and CORS-free.
$env:VITE_API_BASE = ''
$env:VITE_BRAND_NAME = $brand

# ── Workspace shell → wwwroot/app ────────────────────────────────────────────────────────────────
# `build:app` (--mode app) is the standalone SPA. Plain `build` produces the npm LIBRARY, which has
# no index.html and would leave the host serving a 404 at /.
Push-Location (Join-Path $frontend 'plenipo-ui')
try {
    Write-Host "Building @plenipo/ui (app shell, brand=$brand)…"
    pnpm build:app
    if ($LASTEXITCODE -ne 0) { throw "@plenipo/ui build failed ($LASTEXITCODE)." }
}
finally { Pop-Location }

# ── Admin console → wwwroot/admin ────────────────────────────────────────────────────────────────
# admin-ui aliases @plenipo/ui to source, so VITE_API_BASE is inlined from ITS build, not the one
# above — it has to be exported for this build too.
Push-Location (Join-Path $frontend 'admin-ui')
try {
    Write-Host 'Building @plenipo/admin-ui…'
    pnpm build
    if ($LASTEXITCODE -ne 0) { throw "@plenipo/admin-ui build failed ($LASTEXITCODE)." }
}
finally { Pop-Location }

$wwwroot = Join-Path $repoRoot 'src/Auditworthy.Host/wwwroot'
$targets = @(
    @{ Src = Join-Path $frontend 'plenipo-ui/dist-app'; Dest = Join-Path $wwwroot 'app' },
    @{ Src = Join-Path $frontend 'admin-ui/dist';       Dest = Join-Path $wwwroot 'admin' }
)

foreach ($t in $targets) {
    if (-not (Test-Path $t.Src)) { throw "Expected build output missing: $($t.Src)" }
    # Remove first: asset filenames are content-hashed, so copying over a previous build leaves the
    # old bundles behind as untracked litter that the index.html no longer references.
    if (Test-Path $t.Dest) { Remove-Item -Recurse -Force $t.Dest }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $t.Dest) | Out-Null
    Copy-Item -Recurse -Force $t.Src $t.Dest
    Write-Host "  -> $($t.Dest)"
}

# Fail loudly rather than shipping a bundle that talks to :8080. This is the exact defect that made
# the UI look "broken but served": assets 200, every API call refused.
#
# Assert the POSITIVE marker. Vite inlines the env var at the configureClient call site, so a good
# bundle contains `baseUrl:""` and a bundle built without VITE_API_BASE contains `baseUrl:void 0`
# there instead. Searching for the "http://localhost:8080" literal would NOT work: it is the `??`
# fallback inside normalizeApiBase's own body and is therefore present in both builds.
$appBundle = Get-ChildItem (Join-Path $wwwroot 'app/assets') -Filter '*.js' | Select-Object -First 1
if (-not $appBundle) {
    throw "No JS bundle found in wwwroot/app/assets — the app build produced nothing to serve."
}
if (-not (Select-String -Path $appBundle.FullName -Pattern 'baseUrl:""' -SimpleMatch -Quiet)) {
    throw @"
$($appBundle.Name) did not bake in the same-origin API base: VITE_API_BASE never reached the build.
The shell will serve, then every API call will go to http://localhost:8080 and be refused.
"@
}

Write-Host ''
Write-Host 'UI vendored. Rebuild the host so wwwroot lands in bin/:'
Write-Host '  dotnet build src/Auditworthy.Host/Auditworthy.Host.csproj'
