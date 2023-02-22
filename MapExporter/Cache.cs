using System.Collections.Generic;

namespace MapExporter;

sealed class Cache
{
    public readonly Dictionary<string, MapContent> metadata = new();   // region name -> map content
    public readonly Dictionary<string, RoomSettings> settings = new(); // room name -> settings
}
