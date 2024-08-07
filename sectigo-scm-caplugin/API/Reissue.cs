using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class ReissueRequest
	{
		public string csr { get; set; }
		public string reason { get; set; }
		public string commonName { get; set; }
		public string[] subjectAlternativeNames { get; set; }
	}
}
