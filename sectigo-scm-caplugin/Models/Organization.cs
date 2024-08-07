using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Models
{
	public class Organization
	{
		public int id { get; set; }
		public string name { get; set; }
		public List<Department> departments { get; set; }
	}
}
