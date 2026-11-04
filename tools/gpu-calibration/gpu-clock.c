// Tool code (tools/README.md, "GPU calibration"). It never ships, and no engine code depends on it. It samples the
// GPU's performance states through IOReport, which needs no root (powermetrics does), while a command runs, and prints
// the GPU clock of each sample and of the whole run.
//
// Build (outside the dev shell, with Xcode's command line tools):
//   xcrun clang -O2 -o captures/3ug8/bin/gpu-clock tools/gpu-calibration/gpu-clock.c -framework CoreFoundation \
//     -framework IOKit -lIOReport
// Run:
//   gpu-clock <interval ms> <command> [arguments]
//
// IOReport's group "GPU Stats", subgroup "GPU Performance States", channel "GPUPH", counts the time that the GPU spends
// in each state: OFF, and P1 to P15, in ticks of the 24 MHz timebase. The states P1 up to the count of nonzero entries
// map to the frequencies of the property "voltage-states9" of the power manager's node in the I/O registry, "pmgr":
// pairs of a frequency in Hz and a voltage in mV, the first pair of 0 Hz being the off state. This is how sudoless
// monitors such as macmon read the GPU clock. Apple does not document it.
//
// Each sample line: "sample t=<s> active=<share of the interval not OFF> mhz=<mean clock over the active time>
// states=<share of the active time in each state>". The summary line "run active_ms=... mhz=..." gives the mean clock
// over all active time of the run, and "states=" its split. The command's output goes to stdout unchanged.

#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOKitLib.h>
#include <signal.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

extern char **environ;

typedef struct IOReportSubscription *IOReportSubscriptionRef;
extern CFDictionaryRef IOReportCopyChannelsInGroup(CFStringRef, CFStringRef, uint64_t, uint64_t, uint64_t);
extern IOReportSubscriptionRef IOReportCreateSubscription(void *, CFMutableDictionaryRef, CFMutableDictionaryRef *,
                                                          uint64_t, CFTypeRef);
extern CFDictionaryRef IOReportCreateSamples(IOReportSubscriptionRef, CFMutableDictionaryRef, CFTypeRef);
extern CFDictionaryRef IOReportCreateSamplesDelta(CFDictionaryRef, CFDictionaryRef, CFTypeRef);
extern CFStringRef IOReportChannelGetChannelName(CFDictionaryRef);
extern int IOReportStateGetCount(CFDictionaryRef);
extern CFStringRef IOReportStateGetNameForIndex(CFDictionaryRef, int);
extern int64_t IOReportStateGetResidency(CFDictionaryRef, int);

enum { MaxStates = 16 };

// The GPU's frequencies in MHz, index 0 the off state, from the I/O registry. Returns the count of entries.
static int frequencies(double *mhz) {
    io_registry_entry_t pmgr = IORegistryEntryFromPath(kIOMainPortDefault, "IODeviceTree:/arm-io/pmgr");
    if (pmgr == MACH_PORT_NULL) return 0;
    CFDataRef data = IORegistryEntryCreateCFProperty(pmgr, CFSTR("voltage-states9"), kCFAllocatorDefault, 0);
    IOObjectRelease(pmgr);
    if (data == NULL) return 0;
    const uint32_t *pairs = (const uint32_t *)CFDataGetBytePtr(data);
    int count = (int)(CFDataGetLength(data) / 8);
    if (count > MaxStates) count = MaxStates;
    for (int i = 0; i < count; i++) mhz[i] = pairs[2 * i] / 1e6;
    CFRelease(data);
    return count;
}

// The residency of each state of the channel GPUPH in a delta of samples, in ticks. Returns false without it.
static int residency(CFDictionaryRef delta, int64_t *ticks) {
    CFArrayRef channels = CFDictionaryGetValue(delta, CFSTR("IOReportChannels"));
    for (CFIndex i = 0; channels != NULL && i < CFArrayGetCount(channels); i++) {
        CFDictionaryRef channel = CFArrayGetValueAtIndex(channels, i);
        CFStringRef name = IOReportChannelGetChannelName(channel);
        if (name == NULL || CFStringCompare(name, CFSTR("GPUPH"), 0) != kCFCompareEqualTo) continue;
        int count = IOReportStateGetCount(channel);
        for (int s = 0; s < MaxStates; s++) ticks[s] = s < count ? IOReportStateGetResidency(channel, s) : 0;
        return 1;
    }
    return 0;
}

// Prints the shares of the active states, and returns the mean clock over the active time in MHz.
static double describe(const int64_t *ticks, const double *mhz, int states, int64_t *activeTicks) {
    int64_t active = 0;
    double weighted = 0;
    for (int s = 1; s < states; s++) {
        active += ticks[s];
        weighted += (double)ticks[s] * mhz[s];
    }
    *activeTicks = active;
    return active > 0 ? weighted / (double)active : 0;
}

static void states(const int64_t *ticks, int states, int64_t active) {
    printf(" states=");
    for (int s = 1; s < states; s++) printf("%s%.3f", s > 1 ? "," : "", active > 0 ? (double)ticks[s] / active : 0);
    printf("\n");
}

static double now(void) {
    struct timespec t;
    clock_gettime(CLOCK_MONOTONIC, &t);
    return t.tv_sec + t.tv_nsec * 1e-9;
}

int main(int argc, char **argv) {
    if (argc < 3) {
        fprintf(stderr, "usage: gpu-clock <interval ms> <command> [arguments]\n");
        return 2;
    }
    int interval = atoi(argv[1]);
    double mhz[MaxStates] = {0};
    int count = frequencies(mhz);
    if (count < 2) {
        fprintf(stderr, "The I/O registry gives no GPU frequencies (pmgr, voltage-states9).\n");
        return 1;
    }
    printf("frequencies_mhz=");
    for (int s = 1; s < count; s++) printf("%s%.1f", s > 1 ? "," : "", mhz[s]);
    printf("\n");

    CFDictionaryRef channels = IOReportCopyChannelsInGroup(CFSTR("GPU Stats"), CFSTR("GPU Performance States"), 0, 0, 0);
    if (channels == NULL) {
        fprintf(stderr, "IOReport gives no channel of GPU Performance States.\n");
        return 1;
    }
    CFMutableDictionaryRef wanted = CFDictionaryCreateMutableCopy(kCFAllocatorDefault, 0, channels);
    CFMutableDictionaryRef subscribed = NULL;
    IOReportSubscriptionRef subscription = IOReportCreateSubscription(NULL, wanted, &subscribed, 0, NULL);
    if (subscription == NULL) {
        fprintf(stderr, "IOReport refused the subscription.\n");
        return 1;
    }

    fflush(stdout);
    pid_t child;
    if (posix_spawnp(&child, argv[2], NULL, NULL, &argv[2], environ) != 0) {
        perror("posix_spawnp");
        return 1;
    }
    double start = now();
    CFDictionaryRef first = IOReportCreateSamples(subscription, subscribed, NULL);
    CFDictionaryRef last = first;
    CFRetain(first);
    int status = 0;
    for (;;) {
        usleep(interval * 1000);
        pid_t done = waitpid(child, &status, WNOHANG);
        CFDictionaryRef sample = IOReportCreateSamples(subscription, subscribed, NULL);
        CFDictionaryRef delta = IOReportCreateSamplesDelta(last, sample, NULL);
        int64_t ticks[MaxStates] = {0}, active = 0, total = 0;
        if (residency(delta, ticks)) {
            double clock = describe(ticks, mhz, count, &active);
            for (int s = 0; s < count; s++) total += ticks[s];
            printf("sample t=%.3f active=%.3f mhz=%.1f", now() - start, total > 0 ? (double)active / total : 0, clock);
            states(ticks, count, active);
            fflush(stdout);
        }
        CFRelease(delta);
        CFRelease(last);
        last = sample;
        if (done == child) break;
    }
    CFDictionaryRef whole = IOReportCreateSamplesDelta(first, last, NULL);
    int64_t ticks[MaxStates] = {0}, active = 0;
    if (residency(whole, ticks)) {
        double clock = describe(ticks, mhz, count, &active);
        printf("run seconds=%.3f active_ms=%.1f mhz=%.1f", now() - start, active / 24e3, clock);
        states(ticks, count, active);
    }
    return WIFEXITED(status) ? WEXITSTATUS(status) : 1;
}
