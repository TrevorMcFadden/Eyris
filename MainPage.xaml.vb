Imports Windows.Devices.Enumeration
Imports Windows.Devices.Sensors
Imports Windows.Graphics.Imaging
Imports Windows.Media
Imports Windows.Media.Capture
Imports Windows.Media.Core
Imports Windows.Media.Devices
Imports Windows.Media.MediaProperties
Imports Windows.Storage
Imports Windows.Storage.FileProperties
Imports Windows.Storage.Streams
Imports Windows.System.Display
Imports Windows.UI.Core
Imports Windows.System

Public Class MainPage
    Inherits Page
#Region "Variables"
    Private ReadOnly DI As DisplayInformation = DisplayInformation.GetForCurrentView()
    Private ReadOnly DOS As SimpleOrientationSensor = SimpleOrientationSensor.GetDefault()
    Private DevO As SimpleOrientation = SimpleOrientation.NotRotated
    Private DisO As DisplayOrientations = DisplayOrientations.Portrait
    Private Shared ReadOnly RK As Guid = New Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1")
    Private CF As StorageFolder = Nothing
    Private ReadOnly DR As DisplayRequest = New DisplayRequest()
    Private ReadOnly SMC As SystemMediaTransportControls = SystemMediaTransportControls.GetForCurrentView()
    Private MC As MediaCapture
    Private II As Boolean
    Private IP As Boolean
    Private ACM As Integer = -1
    Private MP As Boolean
    Private EC As Boolean
    Private Const CERTAINTY_CAP As Double = 0.7
    Private AC As AdvancedPhotoCapture
    Private SAE As SceneAnalysisEffect
#End Region
#Region "Advanced Capture Class"
    Public Class AdvancedCaptureContext
        Public CaptureFileName As String
        Public CaptureOrientation As PhotoOrientation
    End Class
#End Region
#Region "Eyris Lifecycle"
    Public Sub New()
        Me.InitializeComponent()
        NavigationCacheMode = NavigationCacheMode.Disabled
        AddHandler Application.Current.Suspending, AddressOf Application_Suspending
        AddHandler Application.Current.Resuming, AddressOf Application_Resuming
        HdrImpactBar.Maximum = CERTAINTY_CAP
    End Sub
    Private Async Sub Application_Suspending(sender As Object, e As SuspendingEventArgs)
        If Frame.CurrentSourcePageType Is GetType(MainPage) Then
            Dim deferral = e.SuspendingOperation.GetDeferral()
            Await CleanupCameraAsync()
            Await CleanupUi()
            deferral.Complete()
        End If
    End Sub
    Private Async Sub Application_Resuming(sender As Object, o As Object)
        If Frame.CurrentSourcePageType Is GetType(MainPage) Then
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
    End Sub
#End Region
#Region "Event Handlers"
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
    Private Async Sub OrientationSensor_OrientationChanged(sender As SimpleOrientationSensor, args As SimpleOrientationSensorOrientationChangedEventArgs)
        If args.Orientation <> SimpleOrientation.Faceup AndAlso args.Orientation <> SimpleOrientation.Facedown Then
            DevO = args.Orientation
            Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub() UpdateControlOrientation())
        End If
    End Sub
    Private Async Sub DisplayInformation_OrientationChanged(sender As DisplayInformation, args As Object)
        DisO = sender.CurrentOrientation
        If IP Then
            Await SetPreviewRotationAsync()
        End If
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub() UpdateControlOrientation())
    End Sub
    Private Async Sub SceneAnalysisEffect_SceneAnalyzed(sender As SceneAnalysisEffect, args As SceneAnalyzedEventArgs)
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub()
                                                                     HdrImpactBar.Value = Math.Min(CERTAINTY_CAP, args.ResultFrame.HighDynamicRange.Certainty)

                                                                     SceneTypeTextBlock.Text = "Scene: " + args.ResultFrame.AnalysisRecommendation.ToString()
                                                                 End Sub)
    End Sub
    Private Async Sub AdvancedCapture_OptionalReferencePhotoCaptured(sender As AdvancedPhotoCapture, args As OptionalReferencePhotoCapturedEventArgs)
        Dim context = TryCast(args.Context, AdvancedCaptureContext)
        Dim referenceName = context.CaptureFileName.Replace(".jpg", "_ReferencePhoto.jpg")
        Using frame = args.Frame
            Dim file = Await CF.CreateFileAsync(referenceName, CreationCollisionOption.GenerateUniqueName)
            Await ReencodeAndSavePhotoAsync(frame, file, context.CaptureOrientation)
        End Using
    End Sub
    Private Sub AdvancedCapture_AllPhotosCaptured(sender As AdvancedPhotoCapture, args As Object)
        Debug.Write("All photos were captured")
    End Sub
    Private Async Sub PhotoButton_Click(sender As Object, e As RoutedEventArgs) Handles PhotoButton.Click
        Await TakeAdvancedCapturePhotoAsync()
    End Sub
    Private Async Sub MediaCapture_Failed(sender As MediaCapture, errorEventArgs As MediaCaptureFailedEventArgs)
        Await CleanupCameraAsync()
        Await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Sub() UpdateUi())
    End Sub
#End Region
    Private Async Function InitializeCameraAsync() As Task
        If MC Is Nothing Then
            Dim cameraDevice = Await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back)
            If cameraDevice Is Nothing Then
                CameraStatusBlock.Text = "Sorry, there was no camera found on your device."
                Return
            End If
            MC = New MediaCapture()
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
                Await StartPreviewAsync()
                If IP Then
                    Await CreateSceneAnalysisEffectAsync()
                    Await EnableAdvancedCaptureAsync()
                End If
            End If
            UpdateUi()
        End If
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
        Dim rotationDegrees As Integer = ConvertDisplayOrientationToDegrees(DisO)
        If MP Then
            rotationDegrees = (360 - rotationDegrees) Mod 360
        End If
        Dim props = MC.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview)
        props.Properties.Add(RK, rotationDegrees)
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
    Private Async Function CreateSceneAnalysisEffectAsync() As Task
        Dim definition = New SceneAnalysisEffectDefinition()
        SAE = CType(Await MC.AddVideoEffectAsync(definition, MediaStreamType.VideoPreview), SceneAnalysisEffect)
        AddHandler SAE.SceneAnalyzed, AddressOf SceneAnalysisEffect_SceneAnalyzed
        SAE.HighDynamicRangeAnalyzer.Enabled = True
    End Function
    Private Async Function CleanSceneAnalysisEffectAsync() As Task
        SAE.HighDynamicRangeAnalyzer.Enabled = False
        RemoveHandler SAE.SceneAnalyzed, AddressOf SceneAnalysisEffect_SceneAnalyzed
        Await MC.RemoveEffectAsync(SAE)
        SAE = Nothing
    End Function
    Private Async Function EnableAdvancedCaptureAsync() As Task
        If AC IsNot Nothing Then
            Return
        End If
        CycleAdvancedCaptureMode()
        AC = Await MC.PrepareAdvancedPhotoCaptureAsync(ImageEncodingProperties.CreateJpeg())
        AddHandler AC.AllPhotosCaptured, AddressOf AdvancedCapture_AllPhotosCaptured
        AddHandler AC.OptionalReferencePhotoCaptured, AddressOf AdvancedCapture_OptionalReferencePhotoCaptured
    End Function
    Private Sub CycleAdvancedCaptureMode()
        ACM = (ACM + 1) Mod MC.VideoDeviceController.AdvancedPhotoControl.SupportedModes.Count
        Dim settings = New AdvancedPhotoCaptureSettings With
            {
                .Mode = MC.VideoDeviceController.AdvancedPhotoControl.SupportedModes(ACM)
            }
        MC.VideoDeviceController.AdvancedPhotoControl.Configure(settings)
        ModeTextBlock.Text = MC.VideoDeviceController.AdvancedPhotoControl.Mode.ToString()
    End Sub
    Private Async Function DisableAdvancedCaptureAsync() As Task
        If AC Is Nothing Then
            Return
        End If
        Await AC.FinishAsync()
        AC = Nothing
        ACM = -1
    End Function
    Private Async Function TakeAdvancedCapturePhotoAsync() As Task
        PhotoButton.IsEnabled = False
        CycleModeButton.IsEnabled = False
        Try
            Dim photoOrientation = ConvertOrientationToPhotoOrientation(GetCameraOrientation())
            Dim fileName = String.Format("EyrisPhoto{0}.jpg", DateTime.Now.ToString("MMddyy"))
            Dim context = New AdvancedCaptureContext With
                {
                    .CaptureFileName = fileName,
                    .CaptureOrientation = photoOrientation
                }
            Dim capture = Await AC.CaptureAsync(context)
            Using frame = capture.Frame
                Dim file = Await CF.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName)
                CameraStatusBlock.Text = "Photo saved to " & file.Path
                Await ReencodeAndSavePhotoAsync(frame, file, photoOrientation)
            End Using
        Catch ex As Exception
        Finally
            UpdateUi()
        End Try
    End Function
    Private Async Function CleanupCameraAsync() As Task
        If II Then
            If IP Then
                Await StopPreviewAsync()
            End If
            If AC IsNot Nothing Then
                Await DisableAdvancedCaptureAsync()
            End If
            If SAE IsNot Nothing Then
                Await CleanSceneAnalysisEffectAsync()
            End If
            II = False
        End If
        If MC IsNot Nothing Then
            RemoveHandler MC.Failed, AddressOf MediaCapture_Failed
            MC.Dispose()
            MC = Nothing
        End If
    End Function
#Region "Helper Methods"
    Private Async Function SetupUiAsync() As Task
        DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape
        DisO = DI.CurrentOrientation
        If DOS IsNot Nothing Then
            DevO = DOS.GetCurrentOrientation()
        End If
        RegisterEventHandlers()
        Dim picturesLibrary = Await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures)
        CF = If(picturesLibrary.SaveFolder, ApplicationData.Current.LocalFolder)
    End Function
    Private Function CleanupUi() As Task
        UnregisterEventHandlers()
        DisplayInformation.AutoRotationPreferences = DisplayOrientations.None
    End Function
    Private Sub UpdateUi()
        PhotoButton.IsEnabled = IP
        CycleModeButton.IsEnabled = IP
    End Sub
    Private Sub RegisterEventHandlers()
        If DOS IsNot Nothing Then
            AddHandler DOS.OrientationChanged, AddressOf OrientationSensor_OrientationChanged
            UpdateControlOrientation()
        End If
        AddHandler CycleModeButton.Click, Sub(sender, args) CycleAdvancedCaptureMode()
        AddHandler DI.OrientationChanged, AddressOf DisplayInformation_OrientationChanged
        AddHandler SMC.PropertyChanged, AddressOf SystemMediaControls_PropertyChanged
    End Sub
    Private Sub UnregisterEventHandlers()
        If DOS IsNot Nothing Then
            RemoveHandler DOS.OrientationChanged, AddressOf OrientationSensor_OrientationChanged
        End If
        RemoveHandler DI.OrientationChanged, AddressOf DisplayInformation_OrientationChanged
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
#Region "Rotation Helpers"
    Private Function GetCameraOrientation() As SimpleOrientation
        If EC Then
            Return SimpleOrientation.NotRotated
        End If
        Dim result = DevO
        If DI.NativeOrientation = DisplayOrientations.Portrait Then
            Select Case result
                Case SimpleOrientation.Rotated90DegreesCounterclockwise
                    result = SimpleOrientation.NotRotated
                Case SimpleOrientation.Rotated180DegreesCounterclockwise
                    result = SimpleOrientation.Rotated90DegreesCounterclockwise
                Case SimpleOrientation.Rotated270DegreesCounterclockwise
                    result = SimpleOrientation.Rotated180DegreesCounterclockwise
                Case SimpleOrientation.NotRotated
                    result = SimpleOrientation.Rotated270DegreesCounterclockwise
            End Select
        End If
        If MP Then
            Select Case result
                Case SimpleOrientation.Rotated90DegreesCounterclockwise
                    Return SimpleOrientation.Rotated270DegreesCounterclockwise
                Case SimpleOrientation.Rotated270DegreesCounterclockwise
                    Return SimpleOrientation.Rotated90DegreesCounterclockwise
            End Select
        End If
        Return result
    End Function
    Private Shared Function ConvertDeviceOrientationToDegrees(orientation As SimpleOrientation) As Integer
        Select Case orientation
            Case SimpleOrientation.Rotated90DegreesCounterclockwise
                Return 90
            Case SimpleOrientation.Rotated180DegreesCounterclockwise
                Return 180
            Case SimpleOrientation.Rotated270DegreesCounterclockwise
                Return 270
            Case Else
                Return 0
        End Select
    End Function
    Private Shared Function ConvertDisplayOrientationToDegrees(orientation As DisplayOrientations) As Integer
        Select Case orientation
            Case DisplayOrientations.Portrait
                Return 90
            Case DisplayOrientations.LandscapeFlipped
                Return 180
            Case DisplayOrientations.PortraitFlipped
                Return 270
            Case Else
                Return 0
        End Select
    End Function
    Private Shared Function ConvertOrientationToPhotoOrientation(orientation As SimpleOrientation) As PhotoOrientation
        Select Case orientation
            Case SimpleOrientation.Rotated90DegreesCounterclockwise
                Return PhotoOrientation.Rotate90
            Case SimpleOrientation.Rotated180DegreesCounterclockwise
                Return PhotoOrientation.Rotate180
            Case SimpleOrientation.Rotated270DegreesCounterclockwise
                Return PhotoOrientation.Rotate270
            Case Else
                Return PhotoOrientation.Normal
        End Select
    End Function
    Private Sub UpdateControlOrientation()
        Dim device As Integer = ConvertDeviceOrientationToDegrees(DevO)
        Dim display As Integer = ConvertDisplayOrientationToDegrees(DisO)
        If DI.NativeOrientation = DisplayOrientations.Portrait Then
            device -= 90
        End If
        Dim angle = (360 + display + device) Mod 360
        Dim transform = New RotateTransform With {.Angle = angle}
        PhotoButton.RenderTransform = transform
        CycleModeButton.RenderTransform = transform
        HdrImpactBar.FlowDirection = If((angle = 180 OrElse angle = 270), FlowDirection.RightToLeft, FlowDirection.LeftToRight)
    End Sub
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
        Me.Frame.Navigate(GetType(EyrisVideo))
    End Sub
#End Region
End Class