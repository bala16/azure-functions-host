// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface ILinuxAppServiceFileSystem
    {
        bool FileExists(string filePath);

        long GetFileSizeBytes(string filePath);

        void CreateDirectory(string directoryPath);

        Task AppendLogs(string filePath, IEnumerable<string> logs);

        void MoveFile(string sourceFile, string destinationFile);

        FileInfo[] ListFiles(string directoryPath, string pattern, SearchOption searchOption);

        void DeleteFile(FileInfo fileInfo);
    }
}
