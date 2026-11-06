#pragma once

#include "mtk/errors.hpp"
#include "mtk/occt_bridge.h"

#include <Message_ProgressIndicator.hxx>
#include <Message_ProgressRange.hxx>

#include <atomic>

namespace mtk {

// Passes the progress of an operation of OpenCASCADE to the caller of the bridge, and its requests to stop back.
// OpenCASCADE calls Show() under a lock of its own, so the caller is never called twice at the same time.
class Progress : public Message_ProgressIndicator {
public:
    explicit Progress(const mtk_progress* progress) : progress_(progress != nullptr ? *progress : mtk_progress{}) {}

    // The range to give the operation: one that reports nothing without a caller to report to.
    Message_ProgressRange start() {
        if (progress_.report == nullptr) {
            return Message_ProgressRange();
        }
        report(0.0);
        return Start();
    }

    // To call after the operation, which may have stopped early without saying so: throws if it was asked to stop.
    void finish() {
        if (cancelled_.load()) {
            throw cancelled();
        }
        if (progress_.report != nullptr) {
            report(1.0);
        }
    }

    bool UserBreak() override { return cancelled_.load(); }

    void Show(const Message_ProgressScope&, const bool force) override {
        // A report per hundredth of the way, at most, so that a fine-grained operation does not call back too often.
        const double done = GetPosition();
        if (force || done - reported_ >= 0.01) {
            report(done);
        }
    }

private:
    void report(double done) {
        reported_ = done;
        if (progress_.report(done, progress_.user_data) != 0) {
            cancelled_.store(true);
        }
    }

    mtk_progress progress_;
    double reported_ = 0.0;
    std::atomic<bool> cancelled_{false};
};

} // namespace mtk
