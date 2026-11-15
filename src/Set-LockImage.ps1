param([Parameter(Mandatory=$true)][string]$ImagePath)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
    $null = [Windows.System.UserProfile.UserProfilePersonalizationSettings,Windows.System.UserProfile,ContentType=WindowsRuntime]
    function Await-Operation($Operation, [Type]$ResultType) {
        $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $task = $method.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
        $task.GetAwaiter().GetResult()
    }
    if (-not [Windows.System.UserProfile.UserProfilePersonalizationSettings]::IsSupported()) { throw 'Lock-screen personalization is not supported on this device.' }
    $file = Await-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
    $ok = Await-Operation ([Windows.System.UserProfile.UserProfilePersonalizationSettings]::Current.TrySetLockScreenImageAsync($file)) ([bool])
    if (-not $ok) {
        $null = [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime]
        $operation = [Windows.System.UserProfile.LockScreen]::SetImageFileAsync($file)
        $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
        $task = $method.Invoke($null, @($operation))
        $null = $task.GetAwaiter().GetResult()
    }
    $null = [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime]
    Write-Output ('Current lock-screen image: ' + [Windows.System.UserProfile.LockScreen]::OriginalImageFile)
    Write-Output 'Lock-screen image updated.'
    exit 0
} catch { Write-Output $_.Exception.Message; exit 1 }
