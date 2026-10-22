namespace UmbrellaManager.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_project_runs()
    {
        Assert.NotEqual(string.Empty, typeof(SmokeTests).Assembly.GetName().Name);
    }
}
