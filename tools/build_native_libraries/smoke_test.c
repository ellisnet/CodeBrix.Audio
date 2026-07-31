/* =============================================================================================
 * smoke_test.c - proves a freshly built codebrix_miniaudio actually works
 * =============================================================================================
 *
 * WHAT IT CHECKS, AND WHY IT IS WORTH HAVING
 *   Checking that the .so contains the right symbols only proves it compiled. This program
 *   loads it the way .NET does - dlopen + dlsym, no link-time dependency - and then drives it
 *   through the exact sequence CodeBrix.Audio.Engine's MiniAudioDecoder uses:
 *
 *     1. sf_has_vorbis()                     must return 1 (an Ogg Vorbis decoder is compiled in)
 *     2. sf_allocate_decoder_config(...)     allocate an output-format config
 *     3. sf_allocate_decoder()               allocate the decoder
 *     4. ma_decoder_init_memory(...)         open an .ogg from memory. This is the PULL-mode
 *                                            path; getting it working is the whole reason the
 *                                            managed decoder can report a length and seek.
 *     5. ma_decoder_get_length_in_pcm_frames must be > 0. In push mode Vorbis reports 0, so a
 *                                            zero here means the pull path silently regressed.
 *     6. ma_decoder_read_pcm_frames(...)     must return frames, and they must not be all
 *                                            zeroes - a decoder that outputs silence "succeeds"
 *                                            at every other check.
 *     7. ma_decoder_seek_to_pcm_frame(...)   seek to the midpoint, then read again.
 *     8. ma_decoder_uninit / sf_free         clean teardown.
 *
 * USAGE
 *   Linux:    cc -O2 -o smoke_test smoke_test.c -ldl
 *   macOS:    cc -O2 -o smoke_test smoke_test.c
 *   Windows:  cl /nologo /O2 smoke_test.c
 *
 *   smoke_test <path-to-library> <path-to-test.ogg>
 *
 * Exit code 0 = every check passed. Any failure prints what broke and exits non-zero.
 *
 * It only loads the library the way the host does, so it works unchanged on all three
 * platforms - LoadLibrary/GetProcAddress on Windows, dlopen/dlsym everywhere else.
 * ============================================================================================= */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
    #include <windows.h>
    #define LIB_HANDLE          HMODULE
    #define LIB_OPEN(path)      LoadLibraryA(path)
    #define LIB_SYM(lib, name)  ((void*)GetProcAddress((lib), (name)))
    #define LIB_CLOSE(lib)      FreeLibrary(lib)
    #define LIB_ERROR()         "LoadLibrary failed (see GetLastError)"
#else
    #include <dlfcn.h>
    #define LIB_HANDLE          void*
    #define LIB_OPEN(path)      dlopen((path), RTLD_NOW | RTLD_LOCAL)
    #define LIB_SYM(lib, name)  dlsym((lib), (name))
    #define LIB_CLOSE(lib)      dlclose(lib)
    #define LIB_ERROR()         dlerror()
#endif

/* miniaudio constants we need. Values are fixed by miniaudio's public headers. */
#define MA_SUCCESS      0
#define MA_FORMAT_F32   5   /* ma_format_unknown=0, u8, s16, s24, s32, f32 */

/* The calling convention on Windows for these exports is cdecl, which is the default. */
typedef int  (*fn_has_vorbis)(void);
typedef void*(*fn_alloc_decoder)(void);
typedef void*(*fn_alloc_decoder_config)(int outputFormat, unsigned int channels, unsigned int rate);
typedef int  (*fn_decoder_init_memory)(const void* pData, size_t dataSize, const void* pConfig, void* pDecoder);
typedef int  (*fn_decoder_get_length)(void* pDecoder, unsigned long long* pLength);
typedef int  (*fn_decoder_read)(void* pDecoder, void* pOut, unsigned long long frameCount, unsigned long long* pRead);
typedef int  (*fn_decoder_seek)(void* pDecoder, unsigned long long frameIndex);
typedef int  (*fn_decoder_uninit)(void* pDecoder);
typedef void (*fn_free)(void* p);

static int failures = 0;

static void check(int ok, const char* what)
{
    printf("  [%s] %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) {
        failures++;
    }
}

static void* need_symbol(LIB_HANDLE lib, const char* name)
{
    void* sym = LIB_SYM(lib, name);
    if (sym == NULL) {
        printf("  [FAIL] missing exported symbol: %s\n", name);
        failures++;
    }
    return sym;
}

int main(int argc, char** argv)
{
    LIB_HANDLE lib;
    FILE* f;
    long fileSize;
    unsigned char* fileData;
    void* decoder;
    void* config;
    unsigned long long length = 0, framesRead = 0;
    float buffer[4096];
    int result, i, nonZero;

    fn_has_vorbis           has_vorbis;
    fn_alloc_decoder        alloc_decoder;
    fn_alloc_decoder_config alloc_decoder_config;
    fn_decoder_init_memory  decoder_init_memory;
    fn_decoder_get_length   decoder_get_length;
    fn_decoder_read         decoder_read;
    fn_decoder_seek         decoder_seek;
    fn_decoder_uninit       decoder_uninit;
    fn_free                 sf_free_fn;

    if (argc < 3) {
        fprintf(stderr, "usage: %s <library.so> <test.ogg>\n", argv[0]);
        return 2;
    }

    printf("smoke test: %s\n", argv[1]);
    printf("input     : %s\n", argv[2]);

    /* --- load the library exactly the way .NET's NativeLibrary does --- */
    lib = LIB_OPEN(argv[1]);
    if (lib == NULL) {
        printf("  [FAIL] loading the library: %s\n", LIB_ERROR());
        return 1;
    }
    printf("  [ok] loaded the library\n");

    has_vorbis           = (fn_has_vorbis)          need_symbol(lib, "sf_has_vorbis");
    alloc_decoder        = (fn_alloc_decoder)       need_symbol(lib, "sf_allocate_decoder");
    alloc_decoder_config = (fn_alloc_decoder_config)need_symbol(lib, "sf_allocate_decoder_config");
    decoder_init_memory  = (fn_decoder_init_memory) need_symbol(lib, "ma_decoder_init_memory");
    decoder_get_length   = (fn_decoder_get_length)  need_symbol(lib, "ma_decoder_get_length_in_pcm_frames");
    decoder_read         = (fn_decoder_read)        need_symbol(lib, "ma_decoder_read_pcm_frames");
    decoder_seek         = (fn_decoder_seek)        need_symbol(lib, "ma_decoder_seek_to_pcm_frame");
    decoder_uninit       = (fn_decoder_uninit)      need_symbol(lib, "ma_decoder_uninit");
    sf_free_fn           = (fn_free)                need_symbol(lib, "sf_free");

    /* Also required by the managed layer, checked for presence only. */
    need_symbol(lib, "ma_decoder_init");
    need_symbol(lib, "ma_device_init");
    need_symbol(lib, "sf_get_devices");

    if (failures > 0) {
        printf("\nFAILED: %d check(s)\n", failures);
        return 1;
    }

    check(has_vorbis() == 1, "sf_has_vorbis() reports an Ogg Vorbis decoder is compiled in");

    /* --- read the .ogg into memory --- */
    f = fopen(argv[2], "rb");
    if (f == NULL) {
        printf("  [FAIL] cannot open input file\n");
        return 1;
    }
    fseek(f, 0, SEEK_END);
    fileSize = ftell(f);
    fseek(f, 0, SEEK_SET);
    fileData = (unsigned char*)malloc((size_t)fileSize);
    if (fileData == NULL || fread(fileData, 1, (size_t)fileSize, f) != (size_t)fileSize) {
        printf("  [FAIL] cannot read input file\n");
        fclose(f);
        return 1;
    }
    fclose(f);

    /* --- drive the decoder the way MiniAudioDecoder does --- */
    config  = alloc_decoder_config(MA_FORMAT_F32, 2, 44100);
    decoder = alloc_decoder();
    check(config != NULL && decoder != NULL, "allocate decoder + config");

    result = decoder_init_memory(fileData, (size_t)fileSize, config, decoder);
    check(result == MA_SUCCESS, "ma_decoder_init_memory on an .ogg (pull mode)");
    if (result != MA_SUCCESS) {
        printf("\nFAILED: decoder init returned %d\n", result);
        return 1;
    }

    result = decoder_get_length(decoder, &length);
    check(result == MA_SUCCESS && length > 0,
          "ma_decoder_get_length_in_pcm_frames > 0 (push mode would report 0)");

    result = decoder_read(decoder, buffer, 1024, &framesRead);
    check(result == MA_SUCCESS && framesRead > 0, "ma_decoder_read_pcm_frames returns frames");

    nonZero = 0;
    for (i = 0; i < (int)(framesRead * 2) && i < 4096; i++) {
        if (buffer[i] != 0.0f) {
            nonZero = 1;
            break;
        }
    }
    check(nonZero, "decoded audio is not all silence");

    result = decoder_seek(decoder, length / 2);
    check(result == MA_SUCCESS, "ma_decoder_seek_to_pcm_frame to the midpoint");

    framesRead = 0;
    result = decoder_read(decoder, buffer, 1024, &framesRead);
    check(result == MA_SUCCESS && framesRead > 0, "reading after a seek returns frames");

    decoder_uninit(decoder);
    sf_free_fn(decoder);
    sf_free_fn(config);
    free(fileData);
    LIB_CLOSE(lib);

    if (failures > 0) {
        printf("\nFAILED: %d check(s)\n", failures);
        return 1;
    }

    printf("\nAll smoke-test checks passed (%llu PCM frames in the test file).\n", length);
    return 0;
}
