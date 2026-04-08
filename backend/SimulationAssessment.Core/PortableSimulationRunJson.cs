using System.Globalization;
using System.Text;

namespace GemmaHackathon.SimulationFramework
{
    internal static class SimulationRunJson
    {
        internal static void AppendIntProperty(StringBuilder builder, string name, int value)
        {
            builder.Append('"');
            builder.Append(JsonText.Escape(name ?? string.Empty));
            builder.Append("\":");
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
