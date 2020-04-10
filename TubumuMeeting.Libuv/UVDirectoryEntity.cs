using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
	public class UVDirectoryEntity
	{
		unsafe internal UVDirectoryEntity(uv_dirent_t entity)
		{
			Name = new string(entity.name);
			Type = entity.type;
		}

		public string Name { get; set; }
		public UVDirectoryEntityType Type { get; set; }
	}
}

