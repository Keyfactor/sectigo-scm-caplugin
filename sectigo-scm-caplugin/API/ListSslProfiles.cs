using Keyfactor.Extensions.CAPlugin.Sectigo.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keyfactor.Extensions.CAPlugin.Sectigo.API
{
	public class ListSslProfilesResponse
	{
		public ListSslProfilesResponse()
		{
			SslProfiles = new List<Profile>();
		}
		public List<Profile> SslProfiles { get; set; }
	}
}
