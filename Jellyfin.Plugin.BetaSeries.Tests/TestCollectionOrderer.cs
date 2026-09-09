using Xunit;

// Disable parallel test execution because tests instantiate Plugin which sets the static Plugin.Instance singleton
[assembly: CollectionBehavior(DisableTestParallelization = true)]
