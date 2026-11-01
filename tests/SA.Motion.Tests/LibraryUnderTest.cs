namespace SA.Motion.Tests;

static class LibraryUnderTest
{
#if SA_LIBRARY_NETSTANDARD
    public const string Framework = ".NETStandard,Version=v2.1";
#else
    public const string Framework = ".NETCoreApp,Version=v10.0";
#endif
}
