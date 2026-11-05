# Local baseline pipeline: restore, build, test.
$ErrorActionPreference = 'Stop'
dotnet restore WindowsCleanupManager.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build WindowsCleanupManager.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test WindowsCleanupManager.sln -c Release --no-build
exit $LASTEXITCODE
