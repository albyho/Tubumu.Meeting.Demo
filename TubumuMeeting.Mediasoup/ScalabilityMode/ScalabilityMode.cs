using System.Text.RegularExpressions;

namespace TubumuMeeting.Mediasoup
{
    public class ScalabilityMode
    {
        private static readonly Regex ScalabilityModeRegex = new Regex("^[LS]([1-9]\\d{0,1})T([1-9]\\d{0,1})(_KEY)?");

        public int SpatialLayers { get; set; }

        public int TemporalLayers { get; set; }

        public bool Ksvc { get; set; }

        public static ScalabilityMode Parse(string scalabilityMode)
        {
            var match = ScalabilityModeRegex.Match(scalabilityMode);
            var result = new ScalabilityMode();
            if (match.Success)
            {
                result.SpatialLayers = int.Parse(match.Groups[0].Value);
                result.TemporalLayers = int.Parse(match.Groups[1].Value);
                // TODO: (alby)bool值转换需要修改
                result.Ksvc = bool.Parse(match.Groups[2].Value);
            }
            else
            {
                result.SpatialLayers = 1;
                result.TemporalLayers = 1;
                result.Ksvc = false;
            }
            return result;
        }
    }
}
