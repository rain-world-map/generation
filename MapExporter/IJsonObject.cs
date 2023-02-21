using System.Collections.Generic;

namespace MapExporter;

interface IJsonObject
{
    Dictionary<string, object> ToJson();
}
