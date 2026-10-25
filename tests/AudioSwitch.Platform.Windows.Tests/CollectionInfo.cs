[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AudioSwitch.Platform.Windows.Tests;

[CollectionDefinition("Windows audio state mutation", DisableParallelization = true)]
public sealed class WindowsAudioStateMutationCollection { }

