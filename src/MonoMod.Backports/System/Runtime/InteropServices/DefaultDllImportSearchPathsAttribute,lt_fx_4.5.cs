﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.InteropServices
{
    [Flags]
    public enum DllImportSearchPath
    {
        UseDllDirectoryForDependencies = 0x100,
        ApplicationDirectory = 0x200,
        UserDirectories = 0x400,
        System32 = 0x800,
        SafeDirectories = 0x1000,
        AssemblyDirectory = 0x2,
        LegacyBehavior = 0x0
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DefaultDllImportSearchPathsAttribute : Attribute
    {
        public DefaultDllImportSearchPathsAttribute(DllImportSearchPath paths)
        {
            Paths = paths;
        }

        public DllImportSearchPath Paths { get; }
    }
}