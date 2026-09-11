$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Transactions
$bin = Join-Path $PSScriptRoot 'bin\Debug\net472'
foreach ($file in 'TaskOTime.App.exe', 'TaskOTime.ViewModel.dll') {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $bin $file))
}
$demoConfig = Get-Content (Join-Path $PSScriptRoot '..\TaskOTime.Cli\demodataconfig.json') -Raw | ConvertFrom-Json
if ($env:TASKOTIME_SMOKE_USER) {
    $configuredCredential = @($demoConfig.users | Where-Object handle -eq $env:TASKOTIME_SMOKE_USER)[0]
    $configuredPassword = if ($env:TASKOTIME_SMOKE_PASSWORD) {
        $env:TASKOTIME_SMOKE_PASSWORD
    }
    elseif ($null -ne $configuredCredential) {
        $configuredCredential.password
    }
    else {
        $null
    }
    $credentials = @([pscustomobject]@{ handle = $env:TASKOTIME_SMOKE_USER; password = $configuredPassword })
}
else {
    $credentials = @($demoConfig.users | Where-Object isAdmin)
}
$desktopConfig = [xml](Get-Content (Join-Path $PSScriptRoot 'App.config') -Raw)
$entityConnection = $desktopConfig.configuration.connectionStrings.add |
    Where-Object name -eq 'TaskOTimeContext' |
    Select-Object -ExpandProperty connectionString
$metadataRoot = Join-Path $bin 'Model'
$entityConnection = $entityConnection.Replace(
    'metadata=.\Model\TaskOTime.csdl|.\Model\TaskOTime.ssdl|.\Model\TaskOTime.msl',
    "metadata=$(Join-Path $metadataRoot 'TaskOTime.csdl')|$(Join-Path $metadataRoot 'TaskOTime.ssdl')|$(Join-Path $metadataRoot 'TaskOTime.msl')")
$oldMode = $env:TASKOTIME_MODE
$oldConnection = $env:TASKOTIME_CONNECTION_STRING
$env:TASKOTIME_MODE = 'Production'
$env:TASKOTIME_CONNECTION_STRING = $entityConnection
$app = New-Object System.Windows.Application
$app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
foreach ($resource in 'Themes/ClassicDark.xaml', 'Resources/Strings.xaml') {
    $app.Resources.MergedDictionaries.Add([System.Windows.Application]::LoadComponent(
        [Uri]::new("/TaskOTime.App;component/$resource", [UriKind]::Relative)))
}
$settingsType = [TaskOTime.App.MainWindow].Assembly.GetType('TaskOTime.App.Properties.Settings')
$settings = $settingsType.GetProperty('Default').GetValue($null)
$settingsType.GetProperty('RestoreMainWindowPlacement').SetValue($settings, $false)
Push-Location $bin
$services = [TaskOTime.App.DesktopServices]::Create()
$login = New-Object TaskOTime.ViewModel.ViewModels.LoginViewModel($services.Authentication)
$replacementPassword = 'Smoke-' + [Guid]::NewGuid().ToString('N')
$scope = New-Object System.Transactions.TransactionScope
try {
    $session = $null
    $loginErrors = @()
    foreach ($credential in $credentials) {
        $userName = $credential.handle
        $password = $credential.password
        if ([string]::IsNullOrWhiteSpace($password)) {
            $loginErrors += "$userName`: no smoke password configured"
            continue
        }
        if ($login.Login($userName, $password)) {
            $session = $login.Session
            break
        }
        if ($login.MustChangePassword -and $login.ChangeTemporaryPassword($password, $replacementPassword)) {
            $session = $login.Session
            break
        }
        $loginErrors += "$userName`: $($login.ErrorMessage)"
        $login.Logout()
    }
    if ($null -eq $session) {
        throw "SQL service login failed for configured administrators: $($loginErrors -join '; ')"
    }
}
finally {
    $scope.Dispose()
}
$tenant = $services.TenantFor($session)
$vm = $services.CreateMain($session)
$window = New-Object TaskOTime.App.MainWindow($vm, $services, $session)
$window.ApplyTemplate() | Out-Null
$window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::DataBind)
function Find-TimeList($node) {
    if ($node -is [System.Windows.Controls.ListView]) { return $node }
    if ($node -is [System.Windows.DependencyObject]) {
        foreach ($child in [System.Windows.LogicalTreeHelper]::GetChildren($node)) {
            $found = Find-TimeList $child
            if ($null -ne $found) { return $found }
        }
    }
    return $null
}
$list = Find-TimeList $window
if ($null -eq $list) { throw 'Original time list not found' }
if (-not [Object]::ReferenceEquals($list.ItemsSource, $vm.TimeCollection.TimeItems)) {
    throw "Collection identity lost: ItemsSource=$($list.ItemsSource), DataContext=$($list.DataContext)"
}
if ($vm.TimeCollection.Categories.Count -eq 0 -or $null -eq $vm.TimeCollection.SelectedCategory) {
    throw 'SQL booking categories were not loaded into the time-entry view model'
}
$access = New-Object TaskOTime.AppServer.Models.TimeBookingAccessContextDto
$access.IdTenant = $session.IdTenant
$access.IdActingUser = $session.IdUser
$access.IdBookingUser = $session.IdUser
$probeDate = [DateTime]::Today.AddYears(10)
$dayRequest = New-Object TaskOTime.AppServer.Models.GetBookingDayRequest
$dayRequest.AccessContext = $access
$dayRequest.BookingDate = $probeDate
$beforeCount = $services.Bookings.GetBookingDay($dayRequest).Value.Items.Count
$bookingScope = New-Object System.Transactions.TransactionScope
try {
    $categoryProbeVm = $services.CreateMain($session)
    $categoryProbeVm.SelectedDate = $probeDate
    $script:sqlEntryRequest = $null
    $entryHandler = [System.EventHandler[TaskOTime.ViewModel.ViewModels.TimeEntryEditRequestEventArgs]] {
        param($eventSender, $eventArgs)
        $script:sqlEntryRequest = $eventArgs
    }
    $categoryProbeVm.TimeCollection.add_TimeEntryEditRequested($entryHandler)
    $categoryProbeVm.TimeCollection.AddCommand.Execute($null)
    $categoryProbeVm.TimeCollection.remove_TimeEntryEditRequested($entryHandler)
    if ($null -eq $script:sqlEntryRequest) {
        throw 'SQL category booking did not raise the editor request'
    }
    $probeTitle = 'SQL category probe ' + [Guid]::NewGuid().ToString('N')
    $selectedCategoryId = $categoryProbeVm.TimeCollection.SelectedCategory.IdCategory
    $script:sqlEntryRequest.SaveAction.Invoke(
        $probeDate.AddHours(1), $probeTitle, 'transactional category verification', $false)
    $categoryDay = $services.Bookings.GetBookingDay($dayRequest).Value
    $categoryItem = @($categoryDay.Items | Where-Object ShortTitle -eq $probeTitle)[0]
    if ($null -eq $categoryItem -or $categoryItem.IdCategory -ne $selectedCategoryId) {
        throw 'SQL normal booking did not use the selected category'
    }

    $vm.SelectedDate = $probeDate
    $errandButton = $window.FindName('CommandStripErrandButton')
    if ($null -eq $errandButton -or $null -ne $errandButton.Command) {
        throw 'Errand command-strip forwarding button was not constructed correctly'
    }
    $errandButton.RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Button]::ClickEvent))
    $window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::DataBind)
    $duringCount = $services.Bookings.GetBookingDay($dayRequest).Value.Items.Count
    if ($duringCount -ne $beforeCount + 2 -or $vm.TimeCollection.TimeItems.Count -ne $duringCount) {
        throw "SQL errand forwarding did not mutate both service and list: before=$beforeCount during=$duringCount list=$($vm.TimeCollection.TimeItems.Count)"
    }
}
finally {
    $bookingScope.Dispose()
}
$afterRollbackCount = $services.Bookings.GetBookingDay($dayRequest).Value.Items.Count
if ($afterRollbackCount -ne $beforeCount) {
    throw "SQL errand verification was not rolled back: before=$beforeCount after=$afterRollbackCount"
}
$master = New-Object TaskOTime.ViewModel.Views.MasterDataWindow
$masterVm = New-Object TaskOTime.ViewModel.ViewModels.MasterDataViewModel($master, $tenant, $session.IdUser, $services.Admin, $services.Users, $services.Bookings, 1)
$loginWindow = New-Object TaskOTime.App.LoginWindow($login, $services.ModeDescription)
$options = New-Object TaskOTime.App.OptionsDialog($vm.Options)
$window.Close()
$master.Close()
$loginWindow.Close()
$options.Close()
$login.Logout()
$app.Shutdown()
Pop-Location
$env:TASKOTIME_MODE = $oldMode
$env:TASKOTIME_CONNECTION_STRING = $oldConnection
Write-Output "SQL/WPF smoke passed for $userName and tenant '$($tenant.TenantName)': login and booking probes rolled back, selected categories persisted, command-strip forwarding mutated the service/list, exact ListView collection identity retained, and dialogs constructed."
