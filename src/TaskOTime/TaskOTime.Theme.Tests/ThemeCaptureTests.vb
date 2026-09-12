Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Xml
Imports Microsoft.VisualStudio.TestTools.UnitTesting

Namespace TaskOTime.Theme.Tests
    <TestClass>
    Public Class ThemeCaptureTests
        <TestMethod>
        Public Sub Capture_ExplicitTargetProducesThirtyRenderedImagesAndProvenance()
            Dim output = NewOutputPath()
            Try
                Dim result = RunCapture(Arguments(ApplicationRoot(), output))
                Assert.AreEqual(0, result.ExitCode, result.ErrorText)
                Assert.AreEqual(String.Empty, result.ErrorText)
                Assert.AreEqual("THEME-CAPTURE-PASS:" & output, result.Output.Trim())
                Dim manifest As New XmlDocument()
                manifest.Load(Path.Combine(output, "capture.xml"))
                Assert.AreEqual(ApplicationRoot(), manifest.DocumentElement.GetAttribute("applicationRoot"))
                Assert.AreNotEqual(Guid.Empty, Guid.Parse(manifest.DocumentElement.GetAttribute("applicationMvid")))
                Assert.AreNotEqual(Guid.Empty, Guid.Parse(manifest.DocumentElement.GetAttribute("viewModelMvid")))
                Assert.AreEqual("true", manifest.DocumentElement.GetAttribute("sampleData"))
                Dim images = manifest.SelectNodes("/themeCapture/image").Cast(Of XmlElement)().ToList()
                Assert.AreEqual(30, images.Count)
                Assert.AreEqual(31, Directory.GetFiles(output).Length)
                Dim fingerprints As New HashSet(Of String)()
                For Each palette In {"Light", "Dark", "HighContrast"}
                    For Each culture In {"en-US", "de-DE"}
                        For Each scene In {"calendar-selected", "calendar-pending-range", "calendar-disabled", "project-normal", "project-disabled"}
                            Dim fileName = palette.ToLowerInvariant() & "-" & culture & "-" & scene & ".png"
                            Dim image = images.Single(Function(item) item.GetAttribute("file") = fileName)
                            Using hash = SHA256.Create()
                                fingerprints.Add(Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(Path.Combine(output, fileName)))))
                            End Using
                            Using stream = File.OpenRead(Path.Combine(output, fileName))
                                Dim decoder As New PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
                                Dim frame = decoder.Frames.Single()
                                Assert.AreEqual(Integer.Parse(image.GetAttribute("width")), frame.PixelWidth)
                                Assert.AreEqual(Integer.Parse(image.GetAttribute("height")), frame.PixelHeight)
                                Dim bitmap As New FormatConvertedBitmap(frame, PixelFormats.Bgra32, Nothing, 0)
                                Dim pixels(bitmap.PixelWidth * bitmap.PixelHeight * 4 - 1) As Byte
                                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0)
                                Dim colors = Enumerable.Range(0, pixels.Length \ 4).
                                    Select(Function(index) BitConverter.ToInt32(pixels, index * 4)).Distinct().Take(16).Count()
                                Assert.AreEqual(16, colors, "Blank or unrendered capture: " & fileName)
                            End Using
                        Next
                    Next
                Next
                Assert.AreEqual(30, fingerprints.Count, "Palette, culture, and state changes must produce distinct rendered images.")
            Finally
                If Directory.Exists(output) Then Directory.Delete(output, True)
            End Try
        End Sub

        <TestMethod>
        Public Sub Capture_IsOptInAndNeverReusesAnExistingDirectory()
            Dim output = NewOutputPath()
            For Each invalid In {"", "--capture", Arguments(ApplicationRoot(), output) & " --extra"}
                Dim rejected = RunCapture(invalid)
                Assert.AreEqual(64, rejected.ExitCode)
                Assert.AreEqual(String.Empty, rejected.Output)
                Assert.IsFalse(Directory.Exists(output))
            Next
            Directory.CreateDirectory(output)
            Dim sentinel = Path.Combine(output, "sentinel.txt")
            File.WriteAllText(sentinel, "retain")
            Try
                Dim result = RunCapture(Arguments(ApplicationRoot(), output))
                Assert.AreEqual(64, result.ExitCode)
                Assert.AreEqual(String.Empty, result.Output)
                Assert.AreEqual("retain", File.ReadAllText(sentinel))
                Assert.AreEqual(1, Directory.GetFiles(output).Length)
                StringAssert.Contains(result.ErrorText, "existing output is never reused")
            Finally
                Directory.Delete(output, True)
            End Try
        End Sub

        <TestMethod>
        Public Sub Capture_InvalidTargetFailsRatherThanUsingHostSideAssemblies()
            Dim target = NewOutputPath()
            Dim output = NewOutputPath()
            Directory.CreateDirectory(target)
            File.WriteAllText(Path.Combine(target, "TaskOTime.App.dll"), "not an assembly")
            File.WriteAllText(Path.Combine(target, "TaskOTime.ViewModel.dll"), "not an assembly")
            Try
                Dim result = RunCapture(Arguments(target, output))
                Assert.AreNotEqual(0, result.ExitCode)
                Assert.AreEqual(String.Empty, result.Output)
                StringAssert.Contains(result.ErrorText, "BadImageFormatException")
                Assert.IsFalse(Directory.Exists(output))
            Finally
                Directory.Delete(target, True)
            End Try
        End Sub

        Private Shared Function RunCapture(arguments As String) As ThemeProcessTests.HostResult
            Return ThemeProcessTests.RunProcess(
                Path.Combine(AppContext.BaseDirectory, "CaptureHost", "TaskOTime.Theme.CaptureHost.exe"), arguments)
        End Function

        Private Shared Function Arguments(target As String, output As String) As String
            Return "--capture --application-root """ & target & """ --output """ & output & """"
        End Function

        Private Shared Function ApplicationRoot() As String
            Return Path.Combine(AppContext.BaseDirectory, "ThemeHost")
        End Function

        Private Shared Function NewOutputPath() As String
            Dim parent = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            Return Path.Combine(parent, "c-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        End Function
    End Class
End Namespace
