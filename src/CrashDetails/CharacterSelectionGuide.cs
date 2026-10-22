namespace Gorilla.CrashDetails
{
    public static class CharacterSelectionGuide
    {
        public const string Text = "A crash while friends select characters through Parsec does not establish that Parsec caused it. " +
            "Character assets, attires, a plugin, controller input or the streaming/graphics path are possible leads.\n\n" +
            "1. Note the exact characters, attires, arena, selection order, number of controllers and crash time.\n\n" +
            "2. Repeat the same selection locally with Parsec fully closed. If it still crashes, test the same characters and attires separately, then a stock character. " +
            "Check character/roster paths in Duplicate files and use the existing backup and mod-search controls for a suspect CAK.\n\n" +
            "3. If the local test works, connect Parsec and let only the host select the same characters. Next allow one guest controller, then add others individually. " +
            "Keep the characters and selection order the same so the input/streaming difference can be compared.\n\n" +
            "4. If only guest input reproduces it, check Parsec and controller-driver versions from their official sources, and compare one controller/input arrangement at a time. " +
            "If Windows records a graphics failure, also compare streaming/overlay settings one change at a time. No setting is changed automatically.\n\n" +
            "5. After a crash, open Fix My Game > Check Again. Compare the event time, faulting component and exception code with the test you just ran. " +
            "Save Latest crash report.txt and the duplicate report for support. A repeatable difference is evidence to investigate; a faulting DLL or duplicate file alone does not identify the root cause.";
    }
}
