// The native view on Windows: a child window of the window the application gives to the viewer.

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include "viewer.hpp"

#include <windows.h>

#include <stdexcept>
#include <string>

#ifndef WM_DPICHANGED_AFTERPARENT
#define WM_DPICHANGED_AFTERPARENT 0x02E3
#endif

namespace {

constexpr wchar_t window_class[] = L"MtkViewerWindow";
constexpr wchar_t original_procedure_property[] = L"MtkViewerOriginalProcedure";

HINSTANCE this_module() {
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&this_module), &module);
    return module;
}

// Window procedure of the window holding the view, which belongs to the application.
LRESULT CALLBACK holder_procedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    const auto original = reinterpret_cast<WNDPROC>(GetPropW(window, original_procedure_property));
    if (message == WM_NCHITTEST) {
        return HTTRANSPARENT;
    }
    if (message == WM_NCDESTROY) {
        SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(original));
        RemovePropW(window, original_procedure_property);
    }
    return original != nullptr ? CallWindowProcW(original, window, message, wparam, lparam)
                               : DefWindowProcW(window, message, wparam, lparam);
}

// The view lets the mouse through to the window below it (HTTRANSPARENT), so that the application receives the
// events over the view, as on macOS, and raises them on its control. The application may put the view in a window of
// its own (Avalonia does): that window must let the mouse through too. It is subclassed when the view is first hit,
// which also covers a new holder after the view moves to another window.
void let_mouse_through(HWND holder) {
    if (holder == nullptr || GetPropW(holder, original_procedure_property) != nullptr) {
        return;
    }
    SetPropW(holder, original_procedure_property, reinterpret_cast<HANDLE>(GetWindowLongPtrW(holder, GWLP_WNDPROC)));
    SetWindowLongPtrW(holder, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&holder_procedure));
}

class Win32View final : public mtk::NativeView {
public:
    Win32View(mtk::Viewer& viewer, HWND parent) {
        register_window_class();
        // Without a size until the application places it: OpenCASCADE attaches once the view has one. Messages, like
        // WM_SIZE, arrive before CreateWindowExW returns: the window procedure sets window_, and the viewer, which is
        // still being constructed, is connected afterwards.
        if (CreateWindowExW(0, window_class, L"", WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, 0, 0, parent,
                            nullptr, this_module(), this)
            == nullptr) {
            throw std::runtime_error("Cannot create the window of the viewer (error " + std::to_string(GetLastError())
                                     + ").");
        }
        viewer_ = &viewer;
    }

    ~Win32View() override {
        viewer_ = nullptr;
        DestroyWindow(window_);
    }

    void* handle() const override {
        return window_;
    }

    bool is_ready() const override {
        const NCollection_Vec2<int> size = pixel_size();
        return size.x() > 0 && size.y() > 0;
    }

    NCollection_Vec2<int> pixel_size() const override {
        RECT client{};
        GetClientRect(window_, &client);
        return NCollection_Vec2<int>(client.right - client.left, client.bottom - client.top);
    }

    double scale() const override {
        return GetDpiForWindow(window_) / 96.0;
    }

    void disconnect() override {
        viewer_ = nullptr;
    }

private:
    static void register_window_class() {
        static const bool registered = [] {
            WNDCLASSEXW description{};
            description.cbSize = sizeof(description);
            description.style = CS_OWNDC; // OpenGL keeps the device context of the window
            description.lpfnWndProc = &Win32View::procedure;
            description.hInstance = this_module();
            description.lpszClassName = window_class;
            if (RegisterClassExW(&description) == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
                throw std::runtime_error("Cannot register the window class of the viewer (error "
                                         + std::to_string(GetLastError()) + ").");
            }
            return true;
        }();
        (void)registered;
    }

    static LRESULT CALLBACK procedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        if (message == WM_NCCREATE) {
            auto* view = static_cast<Win32View*>(reinterpret_cast<const CREATESTRUCTW*>(lparam)->lpCreateParams);
            view->window_ = window;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(view));
        }
        auto* view = reinterpret_cast<Win32View*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        return view != nullptr ? view->handle_message(message, wparam, lparam)
                               : DefWindowProcW(window, message, wparam, lparam);
    }

    LRESULT handle_message(UINT message, WPARAM wparam, LPARAM lparam) {
        switch (message) {
        case WM_NCHITTEST:
            let_mouse_through(GetParent(window_));
            return HTTRANSPARENT;
        case WM_SIZE:
        case WM_DPICHANGED_AFTERPARENT:
            if (viewer_ != nullptr) {
                viewer_->view_changed();
            }
            return 0;
        case WM_PAINT: {
            PAINTSTRUCT paint;
            BeginPaint(window_, &paint);
            if (viewer_ != nullptr) {
                viewer_->view_exposed();
            }
            EndPaint(window_, &paint);
            return 0;
        }
        case WM_ERASEBKGND:
            return 1; // OpenGL draws the whole window
        case WM_NCDESTROY:
            SetWindowLongPtrW(window_, GWLP_USERDATA, 0);
            return DefWindowProcW(window_, message, wparam, lparam);
        default:
            return DefWindowProcW(window_, message, wparam, lparam);
        }
    }

    mtk::Viewer* viewer_ = nullptr;
    HWND window_ = nullptr;
};

} // namespace

std::unique_ptr<mtk::NativeView> mtk::NativeView::create(Viewer& viewer, void* parent) {
    return std::make_unique<Win32View>(viewer, static_cast<HWND>(parent));
}
