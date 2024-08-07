using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Models
{
	public class Profile
	{
		public int id { get; set; }
		public string name { get; set; }
		public int[] terms { get; set; }
	}
}
