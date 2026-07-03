# Sales Bell for Optimizely Commerce

Ring a physical TP-Link Kasa smart plug (a light, a bell, a siren — whatever's plugged in)
whenever an order is placed in Optimizely Customized Commerce.

This is a **standalone, drop-in sample**. It depends only on public Optimizely + Microsoft
NuGet packages — nothing from any particular site or codebase. Copy the `.cs` files into a
Commerce solution (or reference this project) and it self-registers.

## Files

| File | Purpose |
|---|---|
| `KasaOptions.cs` | Config (TP-Link credentials, device alias, on/off kill switch) |
| `IKasaPlug.cs` / `KasaCloudPlug.cs` | Talks to TP-Link's cloud API (login → getDeviceList → passthrough) |
| `SalesBell.cs` | `ISalesBell.Ring()` — enqueues a blink; non-blocking, never throws |
| `SalesBellWorker.cs` | Background service that drains the queue and blinks the plug |
| `SalesBellOrderListener.cs` | Subscribes to `IOrderEvents.SavedOrder`; guards for purchase orders + de-dupes |
| `SalesBellModule.cs` | `IConfigurableModule` — registers everything and wires the listener |

## Install

1. Copy the `.cs` files into your Commerce site.
2. Add a `SalesBell` section to `appsettings.json` (see `appsettings.sample.json`). Put the
   password in Key Vault / DXP configuration, not source control.
3. Done — `SalesBellModule` self-registers via Optimizely's initialization system.

## Build

Built and verified against **`EPiServer.Commerce.Core` 15.0.2 (.NET 10)**. The order-event
API (`IOrderEvents`) is unchanged on Commerce 14, so it works there too — just retarget the
`TargetFramework` to match your site.

The Optimizely packages come from Optimizely's NuGet feed, not nuget.org:

```
dotnet nuget add source "https://nuget.optimizely.com/feed/packages.svc/" -n Optimizely
dotnet build
```
