#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <thread>
#include <atomic>
#include <fstream>
#include <string>
#include <stdexcept>
extern "C" {
struct DropEvent { wchar_t app[1024]; wchar_t ip[64]; UINT16 port; UINT8 protocol; UINT8 inbound; };
__declspec(dllimport) DWORD __cdecl EShieldOpen(void**);
__declspec(dllimport) DWORD __cdecl EShieldBegin(void*);
__declspec(dllimport) DWORD __cdecl EShieldAdd(void*,const wchar_t*,const wchar_t*,int,UINT64,UINT16,UINT16,int,int);
__declspec(dllimport) DWORD __cdecl EShieldCommit(void*);
__declspec(dllimport) void __cdecl EShieldClose(void*);
__declspec(dllimport) int __cdecl EShieldDrain(void*,DropEvent*,int);
__declspec(dllimport) int __cdecl EShieldTelemetry(void*);
}
struct Listener {
 SOCKET socket; UINT16 port; std::thread worker;
 Listener(bool v6=false){socket=::socket(v6?AF_INET6:AF_INET,SOCK_STREAM,IPPROTO_TCP);sockaddr_storage storage{};int len;
 if(v6){auto& a=*reinterpret_cast<sockaddr_in6*>(&storage);a.sin6_family=AF_INET6;a.sin6_addr=in6addr_loopback;len=sizeof(a);}
 else{auto& a=*reinterpret_cast<sockaddr_in*>(&storage);a.sin_family=AF_INET;a.sin_addr.s_addr=INADDR_ANY;len=sizeof(a);}
 if(bind(socket,reinterpret_cast<sockaddr*>(&storage),len)||listen(socket,10))throw std::runtime_error("listen failed");
 getsockname(socket,reinterpret_cast<sockaddr*>(&storage),&len);port=v6?ntohs(reinterpret_cast<sockaddr_in6*>(&storage)->sin6_port):ntohs(reinterpret_cast<sockaddr_in*>(&storage)->sin_port);
 worker=std::thread([this](){for(;;){auto s=accept(socket,nullptr,nullptr);if(s==INVALID_SOCKET)break;closesocket(s);}});}
 ~Listener(){closesocket(socket);worker.join();}
};
bool Connect(const char* ip,UINT16 port){bool v6=strchr(ip,':')!=nullptr;auto s=socket(v6?AF_INET6:AF_INET,SOCK_STREAM,IPPROTO_TCP);u_long mode=1;ioctlsocket(s,FIONBIO,&mode);sockaddr_storage a{};int len;
 if(v6){auto& v=*reinterpret_cast<sockaddr_in6*>(&a);v.sin6_family=AF_INET6;v.sin6_port=htons(port);InetPtonA(AF_INET6,ip,&v.sin6_addr);len=sizeof(v);}
 else{auto& v=*reinterpret_cast<sockaddr_in*>(&a);v.sin_family=AF_INET;v.sin_port=htons(port);InetPtonA(AF_INET,ip,&v.sin_addr);len=sizeof(v);}
 int r=connect(s,reinterpret_cast<sockaddr*>(&a),len);
 if(r!=0&&WSAGetLastError()==WSAEWOULDBLOCK){fd_set write,except;FD_ZERO(&write);FD_ZERO(&except);FD_SET(s,&write);FD_SET(s,&except);timeval time{1,0};select(0,nullptr,&write,&except,&time);int error=0,len=sizeof(error);getsockopt(s,SOL_SOCKET,SO_ERROR,reinterpret_cast<char*>(&error),&len);r=FD_ISSET(s,&write)&&!FD_ISSET(s,&except)&&error==0?0:-1;}
 closesocket(s);return r==0;
}
struct UdpListener {
 SOCKET socket;UINT16 port;std::thread worker;
 UdpListener(){socket=::socket(AF_INET,SOCK_DGRAM,IPPROTO_UDP);sockaddr_in a{};a.sin_family=AF_INET;bind(socket,reinterpret_cast<sockaddr*>(&a),sizeof(a));int len=sizeof(a);getsockname(socket,reinterpret_cast<sockaddr*>(&a),&len);port=ntohs(a.sin_port);
 worker=std::thread([this](){char b[16];sockaddr_in peer{};int len=sizeof(peer);for(;;){auto n=recvfrom(socket,b,sizeof(b),0,reinterpret_cast<sockaddr*>(&peer),&len);if(n==SOCKET_ERROR)break;sendto(socket,b,n,0,reinterpret_cast<sockaddr*>(&peer),len);}});}
 ~UdpListener(){closesocket(socket);worker.join();}
};
bool Udp(const char* ip,UINT16 port){auto s=socket(AF_INET,SOCK_DGRAM,IPPROTO_UDP);DWORD timeout=500;setsockopt(s,SOL_SOCKET,SO_RCVTIMEO,reinterpret_cast<char*>(&timeout),sizeof(timeout));sockaddr_in a{};a.sin_family=AF_INET;InetPtonA(AF_INET,ip,&a.sin_addr);bind(s,reinterpret_cast<sockaddr*>(&a),sizeof(a));a.sin_port=htons(port);bool sent=sendto(s,"ping",4,0,reinterpret_cast<sockaddr*>(&a),sizeof(a))==4;char b[16];bool received=sent&&recv(s,b,sizeof(b),0)==4;closesocket(s);return received;}
int wmain(int argc,wchar_t** argv){
 if(argc==4&&wcscmp(argv[1],L"--probe")==0){WSADATA w;WSAStartup(MAKEWORD(2,2),&w);char ip[64];WideCharToMultiByte(CP_UTF8,0,argv[2],-1,ip,64,nullptr,nullptr);bool okay=Connect(ip,static_cast<UINT16>(_wtoi(argv[3])));WSACleanup();return okay?0:1;}
 std::ofstream log(argc>1?argv[1]:L"native-test.log");void* engine=nullptr;
 auto check=[&](bool okay,const char* message){log<<(okay?"PASS: ":"FAIL: ")<<message<<std::endl;if(!okay)throw std::runtime_error(message);};
 auto api=[&](DWORD err){if(err){log<<"WFP error: "<<err<<std::endl;throw std::runtime_error("WFP API call failed");}};
 WSADATA data;WSAStartup(MAKEWORD(2,2),&data);
 try {
  Listener server;Listener server6(true);UdpListener udp;wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);
  check(Connect("127.0.0.1",server.port)&&Connect("127.0.0.2",server.port),"baseline: both local destinations reachable");
  api(EShieldOpen(&engine));api(EShieldBegin(engine));api(EShieldAdd(engine,path,L"127.0.0.1",0,20,0,0,0,0));api(EShieldCommit(engine));
  check(!Connect("127.0.0.1",server.port),"selected app + IP is blocked by WFP");
  check(EShieldTelemetry(engine)!=0,"WFP blocked-connection event subscription");
  Sleep(400);DropEvent events[64]{};int drops=EShieldDrain(engine,events,64);
  log<<"Blocked event records received: "<<drops<<std::endl;
  check(Connect("127.0.0.2",server.port),"another destination in the same app stays online");
  check(!Udp("127.0.0.1",udp.port)&&Udp("127.0.0.2",udp.port),"UDP block is scoped to its selected destination");
  auto peer=std::wstring(path);peer=peer.substr(0,peer.find_last_of(L"\\/"))+L"\\NativeFilter.Peer.exe";
  check(CopyFileW(path,peer.c_str(),FALSE)!=0,"create isolated peer executable");
  std::wstring command=L"\""+peer+L"\" --probe 127.0.0.1 "+std::to_wstring(server.port);STARTUPINFOW si{};si.cb=sizeof(si);PROCESS_INFORMATION pi{};
  check(CreateProcessW(nullptr,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&pi)!=0,"start peer app");WaitForSingleObject(pi.hProcess,5000);DWORD exit=1;GetExitCodeProcess(pi.hProcess,&exit);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);DeleteFileW(peer.c_str());
  check(exit==0,"blocking one executable does not block another executable");
  if(Connect("1.1.1.1",443)) {
   api(EShieldBegin(engine));api(EShieldAdd(engine,path,L"1.1.1.1",0,20,0,0,0,0));api(EShieldCommit(engine));
   check(!Connect("1.1.1.1",443),"external TCP destination is blocked");
   int externalDrops=0;for(int i=0;i<30&&externalDrops==0;i++){Sleep(100);externalDrops+=EShieldDrain(engine,events,64);}
   check(externalDrops>0,"blocked external attempts reach approval telemetry");
  } else log<<"SKIP: external TCP baseline unavailable"<<std::endl;
  api(EShieldBegin(engine));api(EShieldAdd(engine,path,L"::1",0,20,0,0,0,1));api(EShieldCommit(engine));
  check(!Connect("::1",server6.port)&&Connect("127.0.0.1",server.port),"IPv6 destination is blocked without blocking IPv4");
  // A low-priority deny scoped to this test executable avoids affecting other apps.
  api(EShieldBegin(engine));api(EShieldAdd(engine,path,nullptr,0,1,0,0,0,0));api(EShieldAdd(engine,path,L"127.0.0.2",1,10,0,0,0,0));api(EShieldCommit(engine));
  check(!Connect("127.0.0.1",server.port)&&Connect("127.0.0.2",server.port),"allow-list exception wins over default deny");
  api(EShieldBegin(engine));api(EShieldAdd(engine,path,nullptr,0,1,0,0,0,0));api(EShieldAdd(engine,path,L"127.0.0.2",1,10,0,0,0,0));api(EShieldAdd(engine,path,L"127.0.0.2",0,20,0,0,0,0));api(EShieldCommit(engine));
  check(!Connect("127.0.0.2",server.port),"explicit block wins over allow-list exception");
  EShieldClose(engine);engine=nullptr;
  check(Connect("127.0.0.1",server.port)&&Connect("127.0.0.2",server.port),"closing dynamic session restores connectivity");
  log<<"ALL NATIVE INTEGRATION CHECKS PASSED"<<std::endl;
 }catch(const std::exception& e){if(engine)EShieldClose(engine);log<<"ERROR: "<<e.what()<<std::endl;WSACleanup();return 1;}
 WSACleanup();return 0;
}
