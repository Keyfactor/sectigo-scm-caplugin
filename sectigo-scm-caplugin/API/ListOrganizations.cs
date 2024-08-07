using Keyfactor.Extensions.CAPlugin.Sectigo.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class ListOrganizationsResponse
	{
		public List<Organization> Organizations { get; set; }
	}
}
