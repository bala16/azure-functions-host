// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxAppServiceFileSystem : ILinuxAppServiceFileSystem
    {
        public bool FileExists(string filePath)
        {
            return new FileInfo(filePath).Exists;
        }

        public long GetFileSizeBytes(string filePath)
        {
            return new FileInfo(filePath).Length;
        }

        public void CreateDirectory(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
        }

        public async Task AppendLogs(string filePath, IEnumerable<string> logs)
        {
            using (var streamWriter = File.AppendText(filePath))
            {
                foreach (var log in logs)
                {
                    await streamWriter.WriteLineAsync(log);
                }
            }
        }

        public void MoveFile(string sourceFile, string destinationFile)
        {
            File.Move(sourceFile, destinationFile);
        }

        public FileInfo[] ListFiles(string directoryPath, string pattern, SearchOption searchOption)
        {
            return new DirectoryInfo(directoryPath).GetFiles(pattern, searchOption);
        }

        public void DeleteFile(FileInfo fileInfo)
        {
            fileInfo.Delete();
        }
    }
}