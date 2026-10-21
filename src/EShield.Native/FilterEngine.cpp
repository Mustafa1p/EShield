#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <fwpmu.h>
#include <rpc.h>
#include <vector>
#include <deque>
#include <mutex>
#include <algorithm>

// User-mode WFP management only. No driver, packet interception, or changes to
// Windows Firewall defaults. Dynamic sessions release all filters on process exit.
struct DropEvent { wchar_t app[1024]; wchar_t ip[64]; UINT16 port; UINT8 protocol; UINT8 inbound; };
struct Engine {
    HANDLE handle = nullptr, subscription = nullptr;
    GUID sublayer{};
    std::vector<UINT64> filters, pending;
    std::mutex mutex;
    std::deque<DropEvent> drops;
    bool restoreEvents = false;
};
static void CALLBACK OnDrop(void* context, const FWPM_NET_EVENT2* event) {
    auto e = static_cast<Engine*>(context);
    if (event->type != FWPM_NET_EVENT_TYPE_CLASSIFY_DROP || !event->classifyDrop) return;
    const auto& h = event->header;
    if (!(h.flags & FWPM_NET_EVENT_FLAG_APP_ID_SET) || !h.appId.data) return;
    std::lock_guard<std::mutex> guard(e->mutex);
    if (std::find(e->filters.begin(), e->filters.end(), event->classifyDrop->filterId) == e->filters.end()) return;
    DropEvent d{};
    size_t len = std::min<size_t>(h.appId.size / sizeof(wchar_t), 1023);
    wmemcpy(d.app, reinterpret_cast<const wchar_t*>(h.appId.data), len);
    if (h.ipVersion == FWP_IP_VERSION_V4) {
        IN_ADDR address{}; address.s_addr = htonl(h.remoteAddrV4);
        InetNtopW(AF_INET, &address, d.ip, 64);
    } else if (h.ipVersion == FWP_IP_VERSION_V6) {
        InetNtopW(AF_INET6, const_cast<UINT8*>(h.remoteAddrV6.byteArray16), d.ip, 64);
    } else return;
    d.port = h.remotePort; d.protocol = h.ipProtocol;
    d.inbound = event->classifyDrop->msFwpDirection == 0x3900; // MS FWP_DIRECTION_IN
    if (e->drops.size() < 10000) e->drops.push_back(d);
}
#define API extern "C" __declspec(dllexport)
API DWORD __cdecl EShieldOpen(Engine** result) {
    *result = nullptr;
    auto e = new Engine();
    FWPM_SESSION0 session{}; session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    session.displayData.name = const_cast<wchar_t*>(L"EShield connection filter");
    DWORD err = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &e->handle);
    if (err) { delete e; return err; }
    UuidCreate(&e->sublayer);
    FWPM_SUBLAYER0 layer{}; layer.subLayerKey = e->sublayer; layer.weight = 0x8000;
    layer.displayData.name = const_cast<wchar_t*>(L"EShield rules");
    err = FwpmSubLayerAdd0(e->handle, &layer, nullptr);
    if (err) { FwpmEngineClose0(e->handle); delete e; return err; }
    FWP_VALUE0* previous = nullptr;
    if (!FwpmEngineGetOption0(e->handle, FWPM_ENGINE_COLLECT_NET_EVENTS, &previous)) {
        if (previous->type == FWP_UINT32 && previous->uint32 == 0) {
            FWP_VALUE0 enabled{}; enabled.type = FWP_UINT32; enabled.uint32 = 1;
            e->restoreEvents = FwpmEngineSetOption0(e->handle, FWPM_ENGINE_COLLECT_NET_EVENTS, &enabled) == 0;
        }
        FwpmFreeMemory0(reinterpret_cast<void**>(&previous));
    }
    FWPM_NET_EVENT_SUBSCRIPTION0 sub{};
    FwpmNetEventSubscribe1(e->handle, &sub, OnDrop, e, &e->subscription);
    *result = e; return 0;
}
API DWORD __cdecl EShieldBegin(Engine* e) {
    auto err = FwpmTransactionBegin0(e->handle, 0); if (err) return err;
    e->pending.clear();
    for (auto id : e->filters) {
        err = FwpmFilterDeleteById0(e->handle, id);
        if (err) { FwpmTransactionAbort0(e->handle); return err; }
    }
    return 0;
}
API DWORD __cdecl EShieldAdd(Engine* e, const wchar_t* app, const wchar_t* ip,
    int permit, UINT64 weight, UINT16 protocol, UINT16 port, int inbound, int v6) {
    FWPM_FILTER0 filter{};
    filter.displayData.name = const_cast<wchar_t*>(L"EShield connection decision");
    filter.layerKey = inbound ? (v6 ? FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6 : FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4)
        : (v6 ? FWPM_LAYER_ALE_AUTH_CONNECT_V6 : FWPM_LAYER_ALE_AUTH_CONNECT_V4);
    filter.subLayerKey = e->sublayer;
    filter.action.type = permit ? FWP_ACTION_PERMIT : FWP_ACTION_BLOCK;
    filter.weight.type = FWP_UINT64; filter.weight.uint64 = &weight;
    FWPM_FILTER_CONDITION0 conditions[4]{};
    FWP_BYTE_BLOB* appId = nullptr;
    FWP_V4_ADDR_AND_MASK v4{}; FWP_V6_ADDR_AND_MASK v6addr{};
    DWORD err = 0;
    if (app && *app) {
        err = FwpmGetAppIdFromFileName0(app, &appId); if (err) return err;
        auto& c = conditions[filter.numFilterConditions++];
        c.fieldKey = FWPM_CONDITION_ALE_APP_ID; c.matchType = FWP_MATCH_EQUAL;
        c.conditionValue.type = FWP_BYTE_BLOB_TYPE; c.conditionValue.byteBlob = appId;
    }
    if (ip && *ip) {
        auto& c = conditions[filter.numFilterConditions++];
        c.fieldKey = FWPM_CONDITION_IP_REMOTE_ADDRESS; c.matchType = FWP_MATCH_EQUAL;
        if (v6) {
            if (InetPtonW(AF_INET6, ip, v6addr.addr) != 1) err = ERROR_INVALID_ADDRESS;
            v6addr.prefixLength = 128; c.conditionValue.type = FWP_V6_ADDR_MASK;
            c.conditionValue.v6AddrMask = &v6addr;
        } else {
            IN_ADDR a{};
            if (InetPtonW(AF_INET, ip, &a) != 1) err = ERROR_INVALID_ADDRESS;
            v4.addr = ntohl(a.s_addr); v4.mask = 0xffffffff;
            c.conditionValue.type = FWP_V4_ADDR_MASK; c.conditionValue.v4AddrMask = &v4;
        }
    }
    if (protocol) {
        auto& c = conditions[filter.numFilterConditions++];
        c.fieldKey = FWPM_CONDITION_IP_PROTOCOL; c.matchType = FWP_MATCH_EQUAL;
        c.conditionValue.type = FWP_UINT8; c.conditionValue.uint8 = static_cast<UINT8>(protocol);
    }
    if (port) {
        auto& c = conditions[filter.numFilterConditions++];
        c.fieldKey = FWPM_CONDITION_IP_REMOTE_PORT; c.matchType = FWP_MATCH_EQUAL;
        c.conditionValue.type = FWP_UINT16; c.conditionValue.uint16 = port;
    }
    filter.filterCondition = conditions;
    UINT64 id{};
    if (!err) err = FwpmFilterAdd0(e->handle, &filter, nullptr, &id);
    if (appId) FwpmFreeMemory0(reinterpret_cast<void**>(&appId));
    if (!err) e->pending.push_back(id);
    return err;
}
API DWORD __cdecl EShieldCommit(Engine* e) {
    std::lock_guard<std::mutex> guard(e->mutex);
    auto err = FwpmTransactionCommit0(e->handle);
    if (!err) e->filters = e->pending;
    return err;
}
API void __cdecl EShieldAbort(Engine* e) { FwpmTransactionAbort0(e->handle); }
API int __cdecl EShieldDrain(Engine* e, DropEvent* events, int count) {
    std::lock_guard<std::mutex> guard(e->mutex); int n = 0;
    while (!e->drops.empty() && n < count) { events[n++] = e->drops.front(); e->drops.pop_front(); }
    return n;
}
API int __cdecl EShieldTelemetry(Engine* e) { return e->subscription != nullptr; }
API void __cdecl EShieldClose(Engine* e) {
    if (!e) return;
    if (e->subscription) FwpmNetEventUnsubscribe0(e->handle, e->subscription);
    if (e->restoreEvents) { FWP_VALUE0 disabled{}; disabled.type = FWP_UINT32; disabled.uint32 = 0; FwpmEngineSetOption0(e->handle, FWPM_ENGINE_COLLECT_NET_EVENTS, &disabled); }
    FwpmEngineClose0(e->handle); delete e;
}
