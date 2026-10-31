// The native view on macOS: an NSView, which the application adds to its window. Compiled with ARC.

#import <AppKit/AppKit.h>

#include "viewer.hpp"

// It handles no mouse events: AppKit passes them on to the next responder, up to the view of the application, which
// raises them on its control as for any other control.
@interface MtkViewerView : NSView
@property(nonatomic, assign) mtk::Viewer* viewer;
@end

@implementation MtkViewerView

// OpenCASCADE needs a window to create its OpenGL context: the viewer attaches once the view is in one.
- (void)viewDidMoveToWindow {
    [super viewDidMoveToWindow];
    [self notifyViewer];
}

// The application sets the frame when its control moves or changes size.
- (void)setFrameSize:(NSSize)size {
    [super setFrameSize:size];
    [self notifyViewer];
}

// Moving the window to a display with another scale changes the size of the surface in pixels.
- (void)viewDidChangeBackingProperties {
    [super viewDidChangeBackingProperties];
    [self notifyViewer];
}

- (void)drawRect:(NSRect)dirtyRect {
    (void)dirtyRect;
    if (self.viewer != nullptr) {
        self.viewer->view_exposed();
    }
}

- (void)notifyViewer {
    if (self.viewer != nullptr) {
        self.viewer->view_changed();
    }
}

@end

namespace {

class CocoaView final : public mtk::NativeView {
public:
    explicit CocoaView(mtk::Viewer& viewer) : view_([[MtkViewerView alloc] initWithFrame:NSZeroRect]) {
        view_.viewer = &viewer;
        // OpenCASCADE does not ask for it: without it the surface has one pixel per point, blurred on Retina displays.
        // OpenGL is deprecated on macOS, but it is what OpenCASCADE draws with.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        view_.wantsBestResolutionOpenGLSurface = YES;
#pragma clang diagnostic pop
    }

    ~CocoaView() override {
        view_.viewer = nullptr;
        [view_ removeFromSuperview];
    }

    void* handle() const override {
        return (__bridge void*)view_;
    }

    bool is_ready() const override {
        const NSSize size = view_.bounds.size;
        return view_.window != nil && size.width >= 1.0 && size.height >= 1.0;
    }

    // The same conversion as OpenCASCADE, which sizes its OpenGL viewport from the view.
    NCollection_Vec2<int> pixel_size() const override {
        const NSSize pixels = [view_ convertSizeToBacking:view_.bounds.size];
        return NCollection_Vec2<int>(static_cast<int>(pixels.width), static_cast<int>(pixels.height));
    }

    double scale() const override {
        return view_.window != nil ? view_.window.backingScaleFactor : 1.0;
    }

    void disconnect() override {
        view_.viewer = nullptr;
    }

private:
    MtkViewerView* view_;
};

} // namespace

std::unique_ptr<mtk::NativeView> mtk::NativeView::create(Viewer& viewer, void* parent) {
    (void)parent; // the application adds the view to its window
    return std::make_unique<CocoaView>(viewer);
}
