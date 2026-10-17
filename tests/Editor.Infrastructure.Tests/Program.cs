using Editor.Infrastructure.Tests;
using Xunit.MicrosoftTestingPlatform;

return await TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).ConfigureAwait(false);
