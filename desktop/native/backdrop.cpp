#include <windows.h>
#include <dispatcherqueue.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
using namespace winrt;
using namespace Windows::UI::Composition;
struct Backdrop {
 Windows::System::DispatcherQueueController queue{nullptr};
 Compositor compositor{nullptr};
 Desktop::DesktopWindowTarget target{nullptr};
 SpriteVisual visual{nullptr};
 CompositionRoundedRectangleGeometry geometry{nullptr};
 Backdrop(HWND hwnd){
  if(!Windows::System::DispatcherQueue::GetForCurrentThread()){
   DispatcherQueueOptions options{sizeof(options),DQTYPE_THREAD_CURRENT,DQTAT_COM_NONE};
   check_hresult(CreateDispatcherQueueController(options,reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(queue))));
  }
  compositor=Compositor();
  auto interop=compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
  check_hresult(interop->CreateDesktopWindowTarget(hwnd,FALSE,reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
  visual=compositor.CreateSpriteVisual();visual.Brush(compositor.CreateHostBackdropBrush());
  geometry=compositor.CreateRoundedRectangleGeometry();
  visual.Clip(compositor.CreateGeometricClip(geometry));target.Root(visual);
 }
 ~Backdrop(){try{if(target)target.Root(nullptr);}catch(...){}geometry=nullptr;visual=nullptr;target=nullptr;compositor=nullptr;}
 void resize(float w,float h,float r,bool visible){visual.IsVisible(visible);visual.Size({w,h});geometry.Size({w,h});geometry.CornerRadius({r,r});}
};
extern "C" __declspec(dllexport) void* CreateBackdrop(HWND hwnd){try{return new Backdrop(hwnd);}catch(...){return nullptr;}}
extern "C" __declspec(dllexport) void ResizeBackdrop(Backdrop* p,float w,float h,float r,int visible){try{if(p)p->resize(w,h,r,visible!=0);}catch(...){}}
extern "C" __declspec(dllexport) void DestroyBackdrop(Backdrop* p){try{delete p;}catch(...){}}
