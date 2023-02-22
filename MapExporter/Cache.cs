using System.Collections.Generic;
#pragma warning restore CS0618 // Type or member is obsolete

namespace MapExporter;

sealed class Cache
{
    public readonly Dictionary<string, MapContent> metadata = new();   // region name -> map content
    public readonly Dictionary<string, RoomSettings> settings = new(); // room name -> settings
}
