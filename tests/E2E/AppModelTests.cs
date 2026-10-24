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

    /// <summary>
    /// Slice 03 gate GT-W: the setup code is generated as 29 characters from the alphabet that leaves
    /// out i, l and o (about 131 bits), and it reaches the Api as configuration. Checked on the app
    /// model without starting it: resolving the parameter's VALUE waits for the app to start
    /// (EXECUTED: the first version of this test hung), so the test draws from the parameter's own
    /// generator and checks that the Api's environment names the parameter.
    /// </summary>
    [Fact]
    public async Task The_setup_code_is_generated_long_and_unambiguous()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(TestContext.Current.CancellationToken);
        var parameter = builder.Resources.OfType<ParameterResource>().Single(p => p.Name == "setup-code");

        Assert.True(parameter.Secret);
        var generator = Assert.IsType<GenerateParameterDefault>(parameter.Default);
        Assert.Matches("^[a-hjkmnp-z]{29}$", generator.GetDefaultValue());

        var api = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "api");
        var context = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run));
        foreach (var callback in api.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await callback.Callback(context);
        Assert.Same(parameter, context.EnvironmentVariables["Enrolment__SetupCode"]);
    }
}
