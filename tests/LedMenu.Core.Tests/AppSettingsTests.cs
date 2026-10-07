using LedMenu.Core.Models;

namespace LedMenu.Core.Tests;

public class AppSettingsTests
{
    [Fact] public void Default_is_valid() => Assert.Null(AppSettings.Validate(new AppSettings()));
    [Fact] public void Null_is_invalid() => Assert.NotNull(AppSettings.Validate(null));
    [Fact] public void Zero_schema_is_invalid() => Assert.NotNull(AppSettings.Validate(new AppSettings { SchemaVersion = 0 }));
    [Fact] public void Newer_schema_is_rejected() =>
        Assert.NotNull(AppSettings.Validate(new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion + 1 }));
}
