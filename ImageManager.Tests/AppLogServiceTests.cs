using System;
using System.IO;
using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests
{
    public class AppLogServiceTests
    {
        [Fact]
        public void LogDirectory_ShouldBeValidAndExist()
        {
            // Act
            string logDir = AppLogService.LogDirectory;

            // Assert
            Assert.False(string.IsNullOrEmpty(logDir));
            Assert.True(Directory.Exists(logDir));
        }

        [Fact]
        public void Log_ShouldAppendMessageToAppLogFile()
        {
            // Arrange
            string testMessage = $"Test log message {Guid.NewGuid()}";

            // Act
            AppLogService.Log(testMessage, "INFO");

            // Assert
            Assert.True(File.Exists(AppLogService.AppLogFilePath));
            string content = File.ReadAllText(AppLogService.AppLogFilePath);
            Assert.Contains(testMessage, content);
        }

        [Fact]
        public void LogException_ShouldRecordExceptionDetails()
        {
            // Arrange
            var ex = new InvalidOperationException("Test exception message", new ArgumentException("Inner exception"));

            // Act
            AppLogService.LogException("UnitTestContext", ex);

            // Assert
            string content = File.ReadAllText(AppLogService.AppLogFilePath);
            Assert.Contains("UnitTestContext", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("Test exception message", content);
            Assert.Contains("Innerexception", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LogFatalCrash_ShouldRecordToCrashLogAndAppLog()
        {
            // Arrange
            var ex = new NullReferenceException("Fatal crash simulation");

            // Act
            AppLogService.LogFatalCrash("UnitTestCrash", ex);

            // Assert
            Assert.True(File.Exists(AppLogService.CrashLogFilePath));
            string crashContent = File.ReadAllText(AppLogService.CrashLogFilePath);
            Assert.Contains("FATAL CRASH DETECTED", crashContent);
            Assert.Contains("UnitTestCrash", crashContent);
            Assert.Contains("Fatal crash simulation", crashContent);

            string appLogContent = File.ReadAllText(AppLogService.AppLogFilePath);
            Assert.Contains("FATAL CRASH DETECTED", appLogContent);
        }
    }
}
