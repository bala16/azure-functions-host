// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxAppServiceFileLoggerTests
    {
        private const string LogDirectoryPath = @"C:\temp\logs";
        private const string LogFileName = "FunctionLog";

        [Fact]
        public async void Writes_Logs_to_Files()
        {
            var fileSystem = new Mock<ILinuxAppServiceFileSystem>(MockBehavior.Strict);

            fileSystem.Setup(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))));
            fileSystem.Setup(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(false);
            fileSystem.Setup(fs => fs.AppendLogs(It.Is<string>(s => MatchesLogFilePath(s)), It.Is<IEnumerable<string>>(logs => MatchesLogs(logs)))).Returns(Task.FromResult(true));

            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false);

            foreach (var log in GetLogs())
            {
                fileLogger.Log(log);
            }

            await fileLogger.InternalProcessLogQueue();

            fileSystem.Verify(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);
            fileSystem.Verify(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
        }

        [Fact]
        public async void Does_Not_Modify_Files_When_No_logs()
        {
            // Expect no methods to be called on ILinuxAppServiceFileSystem
            var fileSystem = new Mock<ILinuxAppServiceFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false);
            await fileLogger.InternalProcessLogQueue();
        }

        [Fact]
        public async void Rolls_Files_If_File_Size_Exceeds_Limit()
        {
            var fileSystem = new Mock<ILinuxAppServiceFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false);

            fileSystem.Setup(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))));
            fileSystem.Setup(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(true);
            fileSystem.Setup(fs => fs.GetFileSizeBytes(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns((fileLogger.MaxFileSizeMb * 1024 * 1024) + 1);
            fileSystem.Setup(fs => fs.MoveFile(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()));
            fileSystem.Setup(fs => fs.ListFiles(It.Is<string>(s => string.Equals(s, LogDirectoryPath)),
                It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly)).Returns(new FileInfo[0]);

            fileLogger.Log("LogMessgae");
            await fileLogger.InternalProcessLogQueue();

            fileSystem.Verify(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);
            fileSystem.Verify(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileSystem.Verify(fs => fs.GetFileSizeBytes(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileSystem.Verify(fs => fs.MoveFile(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()), Times.Once);
            fileSystem.Verify(fs => fs.ListFiles(It.Is<string>(s => string.Equals(s, LogDirectoryPath)),
                It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly), Times.Once);
        }

        [Fact]
        public async void Deletes_Oldest_File_If_Exceeds_Limit()
        {
            var fileSystem = new Mock<ILinuxAppServiceFileSystem>(MockBehavior.Strict);
            var fileLogger = new LinuxAppServiceFileLogger(LogFileName, LogDirectoryPath, fileSystem.Object, false);

            fileSystem.Setup(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))));
            fileSystem.Setup(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s)))).Returns(true);
            fileSystem.Setup(fs => fs.GetFileSizeBytes(It.Is<string>(s => MatchesLogFilePath(s))))
                .Returns((fileLogger.MaxFileSizeMb * 1024 * 1024) + 1);
            fileSystem.Setup(fs => fs.MoveFile(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()));

            var fileCount = fileLogger.MaxFileCount + 1;
            var fileInfos = new FileInfo[fileCount];
            var currentDateTime = DateTime.Now;
            for (int i = 0; i < fileCount; i++)
            {
                fileInfos[i] = new FileInfo(fileLogger.GetCurrentFileName(currentDateTime.AddSeconds(i)));
            }

            fileSystem.Setup(fs => fs.ListFiles(It.Is<string>(s => string.Equals(s, LogDirectoryPath)),
                It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly)).Returns(fileInfos);

            fileSystem.Setup(fs => fs.DeleteFile(It.Is<FileInfo>(fi => fi.FullName.Equals(fileInfos[0].FullName))));

            fileLogger.Log("LogMessgae");
            await fileLogger.InternalProcessLogQueue();

            fileSystem.Verify(fs => fs.CreateDirectory(It.Is<string>(s => string.Equals(s, LogDirectoryPath))), Times.Once);
            fileSystem.Verify(fs => fs.FileExists(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileSystem.Verify(fs => fs.GetFileSizeBytes(It.Is<string>(s => MatchesLogFilePath(s))), Times.Once);
            fileSystem.Verify(fs => fs.MoveFile(It.Is<string>(s => MatchesLogFilePath(s)), It.IsAny<string>()), Times.Once);
            fileSystem.Verify(fs => fs.ListFiles(It.Is<string>(s => string.Equals(s, LogDirectoryPath)),
                It.Is<string>(s => s.StartsWith(LogFileName)), SearchOption.TopDirectoryOnly), Times.Once);
            fileSystem.Verify(fs => fs.DeleteFile(It.Is<FileInfo>(fi => fi.FullName.Equals(fileInfos[0].FullName))), Times.Once);
        }

        private static bool MatchesLogFilePath(string filePath)
        {
            if (!string.Equals(".log", Path.GetExtension(filePath)))
            {
                return false;
            }

            if (!string.Equals(LogFileName, Path.GetFileNameWithoutExtension(filePath)))
            {
                return false;
            }

            if (!string.Equals(LogDirectoryPath, Path.GetDirectoryName(filePath)))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesLogs(IEnumerable<string> actual)
        {
            var expected = GetLogs();

            if (actual.Count() != expected.Count())
            {
                return false;
            }

            return expected.All(e => actual.Contains(e));
        }

        private static IEnumerable<string> GetLogs()
        {
            return new List<string>
            {
                "Message 1", "Msg2", "end"
            };
        }
    }
}
