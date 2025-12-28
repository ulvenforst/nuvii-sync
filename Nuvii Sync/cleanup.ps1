# Nuvii Sync - Emergency Cleanup Script
# Run this if File Explorer is frozen due to orphaned sync root registration

Write-Host "=== Nuvii Sync Emergency Cleanup ===" -ForegroundColor Yellow
Write-Host ""

# Try to unregister the sync root using PowerShell
try {
    # Load the Windows.Storage.Provider namespace
    Add-Type -AssemblyName 'Windows.Storage'
    
    # Get current user SID
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $userSid = $currentUser.User.Value
    
    # Build sync root ID
    $syncRootId = "NuviiSync!$userSid!NuviiAccount"
    
    Write-Host "Sync Root ID: $syncRootId" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Attempting to unregister..." -ForegroundColor Yellow
    
    # Use WinRT to unregister
    $null = [Windows.Storage.Provider.StorageProviderSyncRootManager, Windows.Storage, ContentType=WindowsRuntime]
    [Windows.Storage.Provider.StorageProviderSyncRootManager]::Unregister($syncRootId)
    
    Write-Host "SUCCESS: Sync root unregistered!" -ForegroundColor Green
}
catch {
    Write-Host "Could not unregister via API: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Trying alternative cleanup..." -ForegroundColor Yellow
}

# Alternative: Just remove the client folder if it exists
$clientPath = "C:\Users\ulven\Documents\Programming\lab\Nuvii sync\Nuvii Client"
if (Test-Path $clientPath) {
    Write-Host "Removing client folder: $clientPath" -ForegroundColor Yellow
    try {
        # Use robocopy to empty the folder first (handles special files better)
        $emptyDir = [System.IO.Path]::GetTempPath() + "EmptyDir"
        New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
        robocopy $emptyDir $clientPath /MIR /R:1 /W:1 2>&1 | Out-Null
        Remove-Item $emptyDir -Force -ErrorAction SilentlyContinue
        Remove-Item $clientPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Client folder removed" -ForegroundColor Green
    }
    catch {
        Write-Host "Could not remove folder: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Cleanup Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Now you should:" -ForegroundColor Cyan
Write-Host "1. Restart File Explorer (Ctrl+Shift+Esc > find 'Windows Explorer' > Restart)" -ForegroundColor White
Write-Host "2. Or sign out and sign back in" -ForegroundColor White
Write-Host "3. Create a NEW empty folder for the client (not Nuvii Client)" -ForegroundColor White
Write-Host "4. Add some files to the server folder before starting sync" -ForegroundColor White
