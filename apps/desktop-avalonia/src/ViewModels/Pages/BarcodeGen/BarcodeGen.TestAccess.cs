using System.Collections.Generic;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class BarcodeGen
{
    internal IReadOnlyList<string> TestSessionCodes => _sessionCodes;

    internal void TestTrackSessionCode(string code) => TrackSessionCode(code);
}
