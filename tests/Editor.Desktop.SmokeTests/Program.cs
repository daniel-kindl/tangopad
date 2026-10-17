using Editor.Desktop.SmokeTests;
using Xunit.MicrosoftTestingPlatform;

return await TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).ConfigureAwait(false);
