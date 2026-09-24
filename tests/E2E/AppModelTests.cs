using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Portfolio.E2E;

/// <summary>
/// S-25b: "No test process ever mounts a named volume." Asserted on the app model the test
/// builder produces, before anything starts - so a regression fails here, not by quietly
/// writing test data into the developer's database.
/// </summary>
public class AppModelTests
{
    [Fact]
    public async Task The_test_builder_mounts_no_volume()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(TestContext.Current.CancellationToken);

        var mounts = builder.Resources
            .SelectMany(r => r.Annotations.OfType<ContainerMountAnnotation>().Select(m => (r.Name, m)))
            .Where(x => x.m.Type == ContainerMountType.Volume)
            .Select(x => $"{x.Name}: {x.m.Source} -> {x.m.Target}")
            .ToList();

        Assert.Empty(mounts);
    }
}
