Imports Eyris.CameraStarterKit
Imports Windows.Devices.Enumeration
Imports Windows.Graphics.Imaging
Imports Windows.Media
Imports Windows.Media.Capture
Imports Windows.Media.MediaProperties
Imports Windows.Storage
Imports Windows.Storage.FileProperties
Imports Windows.Storage.Streams
Imports Windows.System
Imports Windows.System.Display
Imports Windows.UI.Core

Public NotInheritable Class EyrisVideo
    Inherits Page
#Region "Variables"
    Private Shared ReadOnly RK As Guid = New Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1")
    Private CF As StorageFolder = Nothing
    Private ReadOnly DR As DisplayRequest = New DisplayRequest()
    Private ReadOnly SMC As SystemMediaTransportControls = SystemMediaTransportControls.GetForCurrentView()
    Private MC As MediaCapture
    Private II As Boolean
    Private IP As Boolean
    Private IR As Boolean
    Private MP As Boolean
    Private EC As Boolean
    Private RH As CameraRotationHelper
#End Region
#Region "Eyris Lifecycle"
    Public Sub New()
        Me.InitializeComponent()
        NavigationCacheMode = NavigationCacheMode.Disabled
        AddHandler Application.Current.Suspending, AddressOf Application_Suspending
        AddHandler Application.Current.Resuming, AddressOf Application_Resuming
    End Sub
    Private Async Sub Application_Suspending(sender As Object, e As SuspendingEventArgs)
        If Frame.CurrentSourcePageType Is GetType(MainPage) Then
            Dim deferral = e.SuspendingOperation.GetDeferral()
            Await CleanupCameraAsync()
            Await CleanupUiAsync()
            deferral.Complete()
        End If
    End Sub
    Private Async Sub Application_Resuming(sender As Object, o As Object)
        If Frame.CurrentSourcePageType Is GetType(EyrisVideo) Then
            Await SetupUiAsync()
            Await InitializeCameraAsync()
        End If
    End Sub
    Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
        Await SetupUiAsync()
        Await InitializeCameraAsync()
    End Sub
    Protected Overrides Async Sub OnNavigatingFrom(e As NavigatingCancelEventArgs)
        Await CleanupCameraAsync()
        Await CleanupUiAsync()
    End Sub
#End Region
#Region "Event handlers"
    Private Async Sub SystemMediaControls_PropertyChanged(sender As SystemMediaTransportControls, args As SystemMediaTransportControlsPropertyChangedEventArgs)
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Async Sub()
                                                                     If args.Property = SystemMediaTransportControlsProperty.SoundLevel AndAlso Frame.CurrentSourcePageType Is GetType(MainPage) Then
                                                                         If sender.SoundLevel = SoundLevel.Muted Then
                                                                             Await CleanupCameraAsync()
                                                                         ElseIf Not II Then
                                                                             Await InitializeCameraAsync()
                                                                         End If
                                                                     End If
                                                                 End Sub)
    End Sub
    Private Async Sub VideoButton_Click(sender As Object, e As RoutedEventArgs) Handles VideoButton.Click
        If Not IR Then
            Await StartRecordingAsync()
        Else
            Await StopRecordingAsync()
        End If
        UpdateCaptureControls()
    End Sub
    Private Async Sub PauseVideoButton_Click(sender As Object, e As RoutedEventArgs) Handles PauseVideoButton.Click
        Await PauseRecordAsync()
        PauseVideoButton.Visibility = Visibility.Collapsed
        ResumeVideoButton.Visibility = Visibility.Visible
    End Sub
    Private Async Sub ResumeVideoButton_Click(sender As Object, e As RoutedEventArgs) Handles ResumeVideoButton.Click
        Await ResumeRecordAsync()
        PauseVideoButton.Visibility = Visibility.Visible
        ResumeVideoButton.Visibility = Visibility.Collapsed
    End Sub
    Private Async Sub MediaCapture_RecordLimitationExceeded(sender As MediaCapture)
        Await StopRecordingAsync()
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub() UpdateCaptureControls())
    End Sub
    Private Async Sub MediaCapture_Failed(sender As MediaCapture, errorEventArgs As MediaCaptureFailedEventArgs)
        Await CleanupCameraAsync()
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub() UpdateCaptureControls())
    End Sub
#End Region
#Region "MediaCapture Methods"
    Private Async Function InitializeCameraAsync() As Task
        If MC Is Nothing Then
            Dim cameraDevice = Await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back)
            If cameraDevice Is Nothing Then
                CameraStatusBlock.Text = "Sorry, there was no camera found on your device."
                Return
            End If
            MC = New MediaCapture()
            AddHandler MC.RecordLimitationExceeded, AddressOf MediaCapture_RecordLimitationExceeded
            AddHandler MC.Failed, AddressOf MediaCapture_Failed
            Dim settings = New MediaCaptureInitializationSettings With {.VideoDeviceId = cameraDevice.Id}
            Try
                Await MC.InitializeAsync(settings)
                II = True
            Catch ex As UnauthorizedAccessException
                CameraStatusBlock.Text = "Eyris was denied access to your camera."
            End Try
            If II Then
                If cameraDevice.EnclosureLocation Is Nothing OrElse cameraDevice.EnclosureLocation.Panel = Windows.Devices.Enumeration.Panel.Unknown Then
                    EC = True
                Else
                    EC = False
                    MP = (cameraDevice.EnclosureLocation.Panel = Windows.Devices.Enumeration.Panel.Front)
                End If
                RH = New CameraRotationHelper(cameraDevice.EnclosureLocation)
                AddHandler RH.OrientationChanged, AddressOf RotationHelper_OrientationChanged
                Await StartPreviewAsync()
                UpdateCaptureControls()
            End If
        End If
    End Function
    Private Async Sub RotationHelper_OrientationChanged(updatePreview As Boolean)
        If updatePreview Then
            Await SetPreviewRotationAsync()
        End If
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Function() UpdateButtonOrientation())
    End Sub
    Private Function UpdateButtonOrientation()
        Dim angle = CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(RH.GetUIOrientation())
        Dim transform = New RotateTransform()
        transform.Angle = angle
        VideoButton.RenderTransform = transform
    End Function
    Private Async Function StartPreviewAsync() As Task
        DR.RequestActive()
        PreviewControl.Source = MC
        PreviewControl.FlowDirection = If(MP, FlowDirection.RightToLeft, FlowDirection.LeftToRight)
        Await MC.StartPreviewAsync()
        IP = True
        If IP Then
            Await SetPreviewRotationAsync()
        End If
    End Function
    Private Async Function SetPreviewRotationAsync() As Task
        If EC Then
            Return
        End If
        Dim rotation = RH.GetCameraPreviewOrientation()
        Dim props = MC.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview)
        props.Properties.Add(RK, CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(rotation))
        Await MC.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, Nothing)
    End Function
    Private Async Function StopPreviewAsync() As Task
        IP = False
        Await MC.StopPreviewAsync()
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub()
                                                                     PreviewControl.Source = Nothing
                                                                     DR.RequestRelease()
                                                                 End Sub)
    End Function
    Private Async Function StartRecordingAsync() As Task
        Try
            Dim videoFile = Await CF.CreateFileAsync("EyrisVideo.mp4", CreationCollisionOption.GenerateUniqueName)
            Dim encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)
            Dim rotationAngle = CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(RH.GetCameraCaptureOrientation())
            encodingProfile.Video.Properties.Add(RK, PropertyValue.CreateInt32(rotationAngle))
            Await MC.StartRecordToStorageFileAsync(encodingProfile, videoFile)
            IR = True
            CameraStatusBlock.Text = "Recording..."
            PauseVideoButton.IsEnabled = True
        Catch ex As Exception
        End Try
    End Function
    Private Async Function PauseRecordAsync() As Task
        Await MC.PauseRecordAsync(Devices.MediaCapturePauseBehavior.ReleaseHardwareResources)
        CameraStatusBlock.Text = "Paused"
    End Function
    Private Async Function ResumeRecordAsync() As Task
        Await MC.ResumeRecordAsync()
        CameraStatusBlock.Text = "Recording..."
    End Function
    Private Async Function StopRecordingAsync() As Task
        CameraStatusBlock.Text = "Finishing..."
        IR = False
        Await MC.StopRecordAsync()
        CameraStatusBlock.Text = "Recording completed"
        PauseVideoButton.IsEnabled = True
    End Function
    Private Async Function CleanupCameraAsync() As Task
        If II Then
            If IR Then
                Await StopRecordingAsync()
            End If

            If IP Then
                Await StopPreviewAsync()
            End If

            II = False
        End If
        If MC IsNot Nothing Then
            RemoveHandler MC.RecordLimitationExceeded, AddressOf MediaCapture_RecordLimitationExceeded
            RemoveHandler MC.Failed, AddressOf MediaCapture_Failed
            MC.Dispose()
            MC = Nothing
        End If
        If RH IsNot Nothing Then
            RemoveHandler RH.OrientationChanged, AddressOf RotationHelper_OrientationChanged
            RH = Nothing
        End If
    End Function
#End Region
#Region "Helper Functions"
    Private Async Function SetupUiAsync() As Task
        DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape
        RegisterEventHandlers()
        Dim picturesLibrary = Await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures)
        CF = If(picturesLibrary.SaveFolder, ApplicationData.Current.LocalFolder)
    End Function
    Private Async Function CleanupUiAsync() As Task
        UnregisterEventHandlers()
        DisplayInformation.AutoRotationPreferences = DisplayOrientations.None
    End Function
    Private Sub UpdateCaptureControls()
        VideoButton.IsEnabled = IP
        StartRecordingIcon.Visibility = If(IR, Visibility.Collapsed, Visibility.Visible)
        StopRecordingIcon.Visibility = If(IR, Visibility.Visible, Visibility.Collapsed)
        If II AndAlso Not MC.MediaCaptureSettings.ConcurrentRecordAndPhotoSupported Then
        End If
    End Sub
    Private Sub RegisterEventHandlers()
        AddHandler SMC.PropertyChanged, AddressOf SystemMediaControls_PropertyChanged
    End Sub
    Private Sub UnregisterEventHandlers()
        RemoveHandler SMC.PropertyChanged, AddressOf SystemMediaControls_PropertyChanged
    End Sub
    Private Shared Async Function FindCameraDeviceByPanelAsync(desiredPanel As Windows.Devices.Enumeration.Panel) As Task(Of DeviceInformation)
        Dim allVideoDevices = Await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)
        Dim desiredDevice As DeviceInformation = allVideoDevices.FirstOrDefault(Function(x) x.EnclosureLocation IsNot Nothing AndAlso x.EnclosureLocation.Panel = desiredPanel)
        Return If(desiredDevice, allVideoDevices.FirstOrDefault())
    End Function
    Private Shared Async Function ReencodeAndSavePhotoAsync(stream As IRandomAccessStream, file As StorageFile, photoOrientation As PhotoOrientation) As Task
        Using inputStream = stream
            Dim decoder = Await BitmapDecoder.CreateAsync(inputStream)
            Using outputStream = Await file.OpenAsync(FileAccessMode.ReadWrite)
                Dim encoder = Await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder)
                Dim properties = New BitmapPropertySet From {{"System.Photo.Orientation", New BitmapTypedValue(photoOrientation, PropertyType.UInt16)}}
                Await encoder.BitmapProperties.SetPropertiesAsync(properties)
                Await encoder.FlushAsync()
            End Using
        End Using
    End Function
#End Region
#Region "Windows User Information"
    Private Async Sub Me_Loaded(ByVal sender As Object, ByVal e As RoutedEventArgs) Handles Me.Loaded
        Dim u As IReadOnlyList(Of User) = Await User.FindAllAsync()
        Dim current = u.Where(Function(p) p.AuthenticationStatus = UserAuthenticationStatus.LocallyAuthenticated AndAlso p.Type = UserType.LocalUser).FirstOrDefault()
        Dim data = Await current.GetPropertyAsync(KnownUserProperties.FirstName)
        Dim em = Await current.GetPropertyAsync(KnownUserProperties.AccountName)
        Dim displayName As String = CStr(data)
        UsernameTextBlock.Text = "Hello " & displayName
        Dim streamReference As IRandomAccessStreamReference = Await current.GetPictureAsync(UserPictureSize.Size64x64)
        If streamReference IsNot Nothing Then
            Dim stream As IRandomAccessStream = Await streamReference.OpenReadAsync()
            Dim bitmapImage As BitmapImage = New BitmapImage()
            bitmapImage.SetSource(stream)
            ProfilePhotoBox.Source = bitmapImage
        End If
    End Sub
#End Region
#Region "ToButton"
    Private Sub ToButton_Click(sender As Object, e As RoutedEventArgs) Handles ToButton.Click
        Me.Frame.Navigate(GetType(MainPage))
    End Sub
#End Region
End Class