﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using static Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultKeyValueConverterFactory : IKeyValueConverterFactory
    {
        private readonly bool _encryptionSupported;
        private static readonly PlaintextKeyValueConverter PlaintextValueConverter = new PlaintextKeyValueConverter(FileAccess.ReadWrite);

        public DefaultKeyValueConverterFactory(bool allowEncryption)
        {
            _encryptionSupported = !allowEncryption && IsEncryptionSupported();
        }

        private static bool IsEncryptionSupported()
        {
            return SystemEnvironment.Instance.IsAppServiceEnvironment() ||
                SystemEnvironment.Instance.GetEnvironmentVariable(AzureWebsiteLocalEncryptionKey) != null;
        }

        public IKeyValueReader GetValueReader(Key key)
        {
            if (key.IsEncrypted)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Read);
            }

            return PlaintextValueConverter;
        }

        public IKeyValueWriter GetValueWriter(Key key)
        {
            if (_encryptionSupported)
            {
                return new DataProtectionKeyValueConverter(FileAccess.Write);
            }

            return PlaintextValueConverter;
        }
    }
}