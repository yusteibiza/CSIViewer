using System.Text.Json;
using CSIViewer.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class LoggingTests
{
    [TestMethod]
    public void Redactor_MasksSecretsAndPreservesDiagnosticText()
    {
        var input =
            "password=SuperSecret token: abc123 Authorization: Bearer aaa.bbb.ccc " +
            "url=https://user:pass@example.com/signal " +
            "private=-----BEGIN PRIVATE KEY-----abc-----END PRIVATE KEY-----";

        var output = SensitiveDataRedactor.Redact(input);

        Assert.IsFalse(output.Contains("SuperSecret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("abc123", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("aaa.bbb.ccc", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("user:pass", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal));
        Assert.IsTrue(output.Contains("url=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Provider_WritesStructuredRecordWithScopeAndRedactedProperties()
    {
        var directory = CreateDirectory();
        var prefix = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new LoggingOptions
            {
                MinimumLevel = LogLevel.Information,
                MaxFileBytes = 1024 * 1024,
                MaxTotalBytes = 2 * 1024 * 1024
            };
            using (var provider = new JsonLinesFileLoggerProvider(directory, options, prefix))
            {
                var logger = provider.CreateLogger("Test.Category");
                using (logger.BeginScope(new Dictionary<string, object?>
                       {
                           ["SessionId"] = "session-1",
                           ["Password"] = "secret"
                       }))
                {
                    logger.LogInformation(
                        new EventId(1234, "TestEvent"),
                        "Hello {Name} Password={Password}",
                        "world",
                        "secret");
                }

                logger.LogDebug("debug should be filtered");
            }

            var file = Directory.GetFiles(directory, prefix + "-*.log*").Single();
            var line = File.ReadAllLines(file).Single();
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            Assert.AreEqual("Information", root.GetProperty("level").GetString());
            Assert.AreEqual(1234, root.GetProperty("eventId").GetInt32());
            Assert.AreEqual("TestEvent", root.GetProperty("event").GetString());
            Assert.AreEqual(
                "session-1",
                root.GetProperty("scopes").GetProperty("SessionId").GetString());
            Assert.AreEqual(
                "***",
                root.GetProperty("scopes").GetProperty("Password").GetString());
            Assert.AreEqual(
                "***",
                root.GetProperty("properties").GetProperty("Password").GetString());
            Assert.IsTrue(
                root.GetProperty("message").GetString()!.Contains("Password=***", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [TestMethod]
    public void Provider_RotatesFilesAndKeepsStorageBounded()
    {
        var directory = CreateDirectory();
        var prefix = "rotate-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new LoggingOptions
            {
                MinimumLevel = LogLevel.Debug,
                MaxFileBytes = 512,
                MaxTotalBytes = 4096,
                RetentionDays = 30
            };
            using (var provider = new JsonLinesFileLoggerProvider(directory, options, prefix))
            {
                var logger = provider.CreateLogger("Rotation.Test");
                for (var index = 0; index < 30; index++)
                {
                    logger.LogInformation("Rotation message {Index} {Payload}", index, new string('x', 200));
                }
            }

            var files = Directory.GetFiles(directory, prefix + "-*.log*");
            Assert.IsTrue(files.Length > 1, "Se esperaba al menos un archivo rotado.");
            Assert.IsTrue(
                files.Sum(file => new FileInfo(file).Length) <= options.MaxTotalBytes * 2,
                "El límite de almacenamiento debe mantenerse acotado.");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [TestMethod]
    public void Provider_DoesNotThrowWhenDirectoryIsInvalid()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            using var provider = new JsonLinesFileLoggerProvider(filePath);
            var logger = provider.CreateLogger("Failure.Test");
            logger.LogCritical("This must not throw");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public void TextLogRotation_RotatesWhenLimitIsReachedAndNeverThrows()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "service.log");
        try
        {
            File.WriteAllBytes(path, new byte[2048]);

            TextLogRotation.RotateIfNeeded(path, 1024);

            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists(path + ".1"));
            TextLogRotation.RotateIfNeeded(path, 1024);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CSIViewerLogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
