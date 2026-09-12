Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks
Imports Microsoft.VisualStudio.TestTools.UnitTesting

Namespace TaskOTime.Theme.Tests
    <TestClass>
    Public Class ThemeProcessTests
        <DataTestMethod>
        <DataRow("calendar-states")>
        <DataRow("calendar-weekday-headers")>
        <DataRow("calendar-range-preview")>
        <DataRow("control-states")>
        <DataRow("list-tab-states")>
        <DataRow("runtime-preferences")>
        <DataRow("maintenance-views")>
        <DataRow("main-window")>
        <DataRow("dialog-options")>
        <DataRow("dialog-time-entry")>
        <DataRow("dialog-login")>
        <DataRow("dialog-shell")>
        <DataRow("dialog-task-list")>
        <DataRow("application-lifetime")>
        Public Sub ThemeCase_MustFinishCleanlyInItsOwnStaProcess(caseName As String)
            Dim result = RunHost("--case " & caseName)
            AssertSuccessful(result, caseName)
        End Sub

        <TestMethod>
        Public Sub HostProtocol_RejectsUnknownMissingAndExtraArguments()
            For Each arguments In {"", "--case", "--case unknown", "--case calendar-states extra", "--Case calendar-states"}
                Dim result = RunHost(arguments)
                Assert.AreEqual(64, result.ExitCode, arguments & ": " & result.ErrorText)
                Assert.AreEqual(String.Empty, result.Output)
                StringAssert.Contains(result.ErrorText, "Usage:")
            Next
        End Sub

        <TestMethod>
        Public Sub HostProtocol_FailedAssertionsCannotReportSuccess()
            Dim result = RunHost("--case self-test-failure")
            Assert.AreEqual(1, result.ExitCode)
            Assert.AreEqual(String.Empty, result.Output)
            StringAssert.Contains(result.ErrorText, "Intentional host failure probe.")
            Assert.ThrowsException(Of AssertFailedException)(Sub() AssertSuccessful(result, "self-test-failure"))
        End Sub

        <TestMethod>
        Public Sub HostProtocol_RejectsLateCrashOrErrorsAfterSuccessMarker()
            Dim result As New HostResult With {
                .ExitCode = 19, .Output = "THEME-CASE-PASS:probe", .ErrorText = String.Empty}
            Assert.ThrowsException(Of AssertFailedException)(Sub() AssertSuccessful(result, "probe"))
            result.ExitCode = 0
            result.ErrorText = "Unhandled Exception during shutdown"
            Assert.ThrowsException(Of AssertFailedException)(Sub() AssertSuccessful(result, "probe"))
        End Sub

        Private Shared Sub AssertSuccessful(result As HostResult, caseName As String)
            Assert.AreEqual(0, result.ExitCode, result.ErrorText & result.Output)
            Assert.AreEqual(String.Empty, result.ErrorText, "The host reported an error despite its exit code.")
            Assert.AreEqual("THEME-CASE-PASS:" & caseName, result.Output.Trim())
        End Sub

        Private Shared Function RunHost(arguments As String) As HostResult
            Dim executable = Path.Combine(AppContext.BaseDirectory, "ThemeHost", "TaskOTime.Theme.TestHost.exe")
            Return RunProcess(executable, arguments)
        End Function

        Friend Shared Function RunProcess(executable As String, arguments As String) As HostResult
            Assert.IsTrue(File.Exists(executable), "Build the requested host before running these tests: " & executable)
            Dim start As New ProcessStartInfo(executable, arguments) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .WorkingDirectory = Path.GetDirectoryName(executable)}
            Using process As New Process With {.StartInfo = start}
                Assert.IsTrue(process.Start(), "The theme host did not start.")
                Dim output = process.StandardOutput.ReadToEndAsync()
                Dim errors = process.StandardError.ReadToEndAsync()
                If Not process.WaitForExit(90000) Then
                    process.Kill()
                    Assert.IsTrue(process.WaitForExit(10000), "The timed-out theme host could not be stopped.")
                    Assert.Fail("The theme host timed out: " & arguments)
                End If
                Task.WaitAll(output, errors)
                Return New HostResult With {.ExitCode = process.ExitCode, .Output = output.Result, .ErrorText = errors.Result}
            End Using
        End Function

        Friend NotInheritable Class HostResult
            Public Property ExitCode As Integer
            Public Property Output As String
            Public Property ErrorText As String
        End Class
    End Class
End Namespace
