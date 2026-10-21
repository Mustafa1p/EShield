# Third-party notices

The MIT license at the root applies to EShield's own source. It does not replace the licenses of dependencies, geographic data, or map services.

| Component | License / attribution | Source |
| --- | --- | --- |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| H.NotifyIcon | MIT | https://github.com/HavenDV/H.NotifyIcon |
| ModernWpf | MIT | https://github.com/Kinnara/ModernWpf |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT | https://github.com/microsoft/perfview |
| Microsoft.Data.Sqlite / System.Management / .NET runtime | MIT; native/transitive components retain their own notices | https://github.com/dotnet |
| MaxMind.Db reader | Apache-2.0 | https://github.com/maxmind/MaxMind-DB-Reader-dotnet |
| SQLitePCLRaw | Apache-2.0; SQLite itself is public domain | https://github.com/ericsink/SQLitePCL.raw |
| Microsoft.Web.WebView2 | Microsoft WebView2 SDK license terms; runtime distributed separately by Microsoft | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| Leaflet 1.9.4 | BSD-2-Clause; bundled license at `src/EShield.App/Resources/Map/LICENSE.txt` | https://github.com/Leaflet/Leaflet |
| DB-IP Lite City database | Creative Commons Attribution 4.0 International; **IP Geolocation by DB-IP** | https://db-ip.com/db/download/ip-to-city-lite |
| OpenStreetMap | © OpenStreetMap contributors; ODbL data attribution and tile usage policy apply | https://www.openstreetmap.org/copyright |
| Country flags | Flagpedia / flagcdn.com; national flag designs are generally public domain, with jurisdiction-specific usage rules | https://flagpedia.net/download |

The DB-IP database is downloaded separately and included in binary packages. Lookups run locally. The application also displays DB-IP and OpenStreetMap attribution in the map and DB-IP attribution in Settings.

NuGet dependencies may include additional runtime components and notices in their packages. Consult the pinned package versions in the project file and the upstream repositories for their complete terms.

Bundled license texts and upstream notices are in [docs/licenses](docs/licenses). The self-contained Windows package also includes the .NET runtime's own notices where provided by the runtime distribution.

TinyShield and Proxyman are referenced only to acknowledge product inspiration. EShield is independently developed and is not an official Windows port, affiliated product, or endorsed project.
