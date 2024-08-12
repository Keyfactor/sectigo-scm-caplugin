using Keyfactor.Extensions.CAPlugin.Sectigo.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class ListCustomFieldsResponse
	{
		public ListCustomFieldsResponse()
		{
			CustomFields = new List<CustomField>();
		}
		public List<CustomField> CustomFields { get; set; }
	}
}
