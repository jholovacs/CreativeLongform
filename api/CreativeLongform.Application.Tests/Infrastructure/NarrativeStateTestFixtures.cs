namespace CreativeLongform.Application.Tests.Infrastructure;

internal static class NarrativeStateTestFixtures
{
    public const string MaraAtKitchen =
        """
        {"schemaVersion":1,"characters":[{"name":"Mara","location":"kitchen","topOfMind":[],"traitsShownNotTold":[]}],"environment":{"setting":"kitchen"}}
        """;

    public const string MaraInHallway =
        """
        {"schemaVersion":1,"characters":[{"name":"Mara","location":"hallway","topOfMind":["the letter"],"traitsShownNotTold":[]}],"environment":{"setting":"hallway"}}
        """;
}
