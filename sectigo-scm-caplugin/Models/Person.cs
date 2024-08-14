using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.Models
{
	public class Person
	{
		public int id { get; set; }
		public string firstName { get; set; }
		public string middleName { get; set; }
		public string lastName { get; set; }
		public string email { get; set; }
		public int organizationId { get; set; }
		public string validationType { get; set; }
		public string phone { get; set; }
		public string[] secondaryEmails { get; set; }
		public string commonName { get; set; }
	}
}
