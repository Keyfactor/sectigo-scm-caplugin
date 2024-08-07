using Newtonsoft.Json;

using Org.BouncyCastle.Tls;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Models
{
    public class Certificate
    {
        public Certificate()
        {
            SubjectAlternativeNames = new List<string>();
        }

        [JsonProperty("sslId")]
        public int Id { get; set; }

        [JsonProperty("commonName")]
        public string CommonName { get; set; }

        [JsonProperty("subjectAlternativeNames")]
        public List<string> SubjectAlternativeNames { get; set; }

        [JsonProperty("serialNumber")]
        public string SerialNumber { get; set; }

        [JsonProperty("certType")]
        public Profile CertType { get; set; }

        public DateTime? requested { get; set; }

        public DateTime? approved { get; set; }

        public DateTime? revoked { get; set; }

        public string status { get; set; }

        public override string ToString()
        {
            return $"sslId:{Id} | commonName:{CommonName} | serialNumber:{SerialNumber}";
        }
    }
}
