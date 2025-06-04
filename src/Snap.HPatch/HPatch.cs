using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static Snap.HPatch.TByteRleType;

namespace Snap.HPatch;

public unsafe partial struct hpatch_TChecksum
{
    [NativeTypeName("const char *(*)()")]
    public delegate*<sbyte*> checksumType;

    [NativeTypeName("hpatch_size_t (*)()")]
    public delegate*<nuint> checksumByteSize;

    [NativeTypeName("hpatch_checksumHandle (*)(struct hpatch_TChecksum *)")]
    public delegate*<hpatch_TChecksum*, void*> open;

    [NativeTypeName("void (*)(struct hpatch_TChecksum *, hpatch_checksumHandle)")]
    public delegate*<hpatch_TChecksum*, void*, void> close;

    [NativeTypeName("void (*)(hpatch_checksumHandle)")]
    public delegate*<void*, void> begin;

    [NativeTypeName("void (*)(hpatch_checksumHandle, const unsigned char *, const unsigned char *)")]
    public delegate*<void*, byte*, byte*, void> append;

    [NativeTypeName("void (*)(hpatch_checksumHandle, unsigned char *, unsigned char *)")]
    public delegate*<void*, byte*, byte*, void> end;
}

public unsafe partial struct __private_hpatch_check_kMaxCompressTypeLength
{
    [NativeTypeName("char[1]")]
    public fixed sbyte _[1];
}

public unsafe partial struct __private_hpatch_check_hpatch_kMaxPackedUIntBytes
{
    [NativeTypeName("char[1]")]
    public fixed sbyte _[1];
}

public unsafe class BytesRleLoadStream
{
    private ulong memCopyLength;
    private ulong memSetLength;
    private byte memSetValue;
    private StreamCacheClip ctrlClip;
    private StreamCacheClip rleCodeClip;

    // _TBytesRle_load_stream_init
    public BytesRleLoadStream()
    {
        ctrlClip = new(null, 0, 0, null, 0);
        rleCodeClip = new(null, 0, 0, null, 0);
    }

    public BytesRleLoadStream(StreamCacheClip ctrlClip, StreamCacheClip rleCodeClip)
    {
        this.ctrlClip = ctrlClip;
        this.rleCodeClip = rleCodeClip;
    }

    // _TBytesRle_load_stream_mem_add
    public bool MemAdd(nuint* decodeSize, byte** outData)
    {
        nuint decodeSizeValue = *decodeSize;
        byte* outDataValue = *outData;
        StreamCacheClip rleCodeClip = this.rleCodeClip;
        ulong memSetLength = this.memSetLength;

        if (memSetLength != 0)
        {
            nuint memSetStep = ((memSetLength <= decodeSizeValue) ? (nuint)(memSetLength) : decodeSizeValue);
            byte byteSetValue = memSetValue;

            if (outDataValue != null)
            {
                if (byteSetValue != 0)
                {
                    HPatch.memSet_add(outDataValue, byteSetValue, unchecked(memSetStep));
                }

                outDataValue += memSetStep;
            }

            decodeSizeValue -= memSetStep;
            this.memSetLength = memSetLength - memSetStep;
        }

        while ((memCopyLength > 0) && (decodeSizeValue > 0))
        {
            byte* rleData;
            nuint decodeStep = rleCodeClip.CacheEnd;

            if (decodeStep > memCopyLength)
            {
                decodeStep = (nuint)(memCopyLength);
            }

            if (decodeStep > decodeSizeValue)
            {
                decodeStep = decodeSizeValue;
            }

            rleData = rleCodeClip.ReadData(decodeStep);
            if (rleData == null)
            {
                return false;
            }

            if ((outDataValue) != null)
            {
                HPatch.addData(outDataValue, rleData, decodeStep);
                outDataValue += decodeStep;
            }

            decodeSizeValue -= decodeStep;
            memCopyLength -= decodeStep;
        }

        *decodeSize = decodeSizeValue;
        *outData = outDataValue;
        return true;
    }

    // _TBytesRle_load_stream_isFinish
    public bool IsFinish()
    {
        return memSetLength == 0
            && memCopyLength == 0
            && rleCodeClip.IsFinish()
            && ctrlClip.IsFinish();
    }

    // _TBytesRle_load_stream_decode_add
    public bool DecodeAdd(byte* out_data, nuint decodeSize)
    {
        if (!MemAdd(&decodeSize, &out_data))
        {
            return false;
        }

        while ((decodeSize > 0) && !ctrlClip.IsFinish())
        {
            TByteRleType type;
            ulong length;
            byte* pType = ctrlClip.AccessData(1);

            if (pType == null)
            {
                return false;
            }

            type = (TByteRleType)((*pType) >> (8 - 2 /* kByteRleType_bit */));

            if (!ctrlClip.UnpackUIntWithTag(&length,2 /*kByteRleType_bit*/))
            {
                return false;
            }

            ++length;
            switch (type)
            {
                case kByteRleType_rle0:
                {
                    memSetLength = length;
                    memSetValue = 0;
                }

                break;
                case kByteRleType_rle255:
                {
                    memSetLength = length;
                    memSetValue = 255;
                }

                break;
                case kByteRleType_rle:
                {
                    byte* pSetValue = rleCodeClip.ReadData(1);

                    if (pSetValue == null)
                    {
                        return false;
                    }

                    memSetValue = *pSetValue;
                    memSetLength = length;
                }

                break;
                case kByteRleType_unrle:
                {
                    memCopyLength = length;
                }

                break;
            }

            if (!MemAdd(&decodeSize, &out_data))
            {
                return false;
            }
        }

        return decodeSize == 0;
    }

    // _rle_decode_skip
    public bool DecodeSkip(ulong copyLength)
    {
        while (copyLength > 0)
        {
            nuint len = (~(nuint)(0));

            if (len > copyLength)
            {
                len = (nuint)(copyLength);
            }

            if (!DecodeAdd(null, len))
            {
                return false;
            }

            copyLength -= len;
        }

        return true;
    }
}

public unsafe class Covers : IHPatchCovers
{
    protected ulong coverCount;
    private ulong oldPosBack;
    private ulong newPosBack;
    protected StreamCacheClip code_inc_oldPosClip;
    protected StreamCacheClip code_inc_newPosClip;
    protected StreamCacheClip code_lengthsClip;
    protected int isOldPosBackNeedAddLength;

    protected Covers()
    {
        newPosBack = 0;
        oldPosBack = 0;
        isOldPosBackNeedAddLength = 0;
    }

    // _covers_init
    public Covers(ulong coverCount, StreamCacheClip code_inc_oldPosClip, StreamCacheClip code_inc_newPosClip, StreamCacheClip code_lengthsClip, int isOldPosBackNeedAddLength)
    {
        this.coverCount = coverCount;
        newPosBack = 0;
        oldPosBack = 0;
        this.code_inc_oldPosClip = code_inc_oldPosClip;
        this.code_inc_newPosClip = code_inc_newPosClip;
        this.code_lengthsClip = code_lengthsClip;
        this.isOldPosBackNeedAddLength = isOldPosBackNeedAddLength;
    }

    // _covers_leaveCoverCount
    public ulong LeaveCoverCount()
    {
        return coverCount;
    }

    // _covers_read_cover
    public bool ReadCover([NotNullWhen(true)] out HPatchCover? cover)
    {
        cover = default;

        ulong oldPosBack = this.oldPosBack;
        ulong newPosBack = this.newPosBack;
        ulong coverCount = this.coverCount;

        if (coverCount > 0)
        {
            this.coverCount = coverCount - 1;
        }
        else
        {
            return false;
        }

        {
            ulong copyLength, coverLength, oldPos, inc_oldPos;
            byte inc_oldPos_sign;
            byte* pSign = code_inc_oldPosClip.AccessData(1);

            if ((pSign) != null)
            {
                inc_oldPos_sign = unchecked((byte)((*pSign) >> (8 - 1 /*kSignTagBit*/)));
            }
            else
            {
                return false;
            }

            if (!code_inc_oldPosClip.UnpackUIntWithTag(&inc_oldPos, 1/*kSignTagBit*/))
            {
                return false;
            }

            oldPos = (inc_oldPos_sign == 0) ? (oldPosBack + inc_oldPos) : (oldPosBack - inc_oldPos);

            if (!code_inc_newPosClip.UnpackUIntWithTag(&copyLength, 0))
            {
                return false;
            }

            if (!code_lengthsClip.UnpackUIntWithTag(&coverLength, 0))
            {
                return false;
            }

            newPosBack += copyLength;
            oldPosBack = oldPos;
            oldPosBack += (isOldPosBackNeedAddLength) != 0 ? coverLength : 0;
            cover = new(oldPos, newPosBack, coverLength);
            newPosBack += coverLength;
        }

        this.oldPosBack = oldPosBack;
        this.newPosBack = newPosBack;
        return true;
    }

    // _covers_is_finish
    public bool IsFinish()
    {
        return code_lengthsClip.IsFinish()
            && code_inc_newPosClip.IsFinish()
            && code_inc_oldPosClip.IsFinish();
    }
}

public unsafe class PackedCovers : Covers
{
    // _packedCovers_open
    public PackedCovers(HDiffHead out_diffHead, HPatchStreamInput serializedDiff, byte* temp_cache, byte* temp_cache_end)
    {
        nuint cacheSize = unchecked((nuint)(temp_cache_end - temp_cache) / 3);

        {
            ulong diffPos0;

            if (!out_diffHead.ReadDiffHead(serializedDiff))
            {
                throw new InvalidOperationException();
            }

            diffPos0 = out_diffHead.HeadEndPos;
            code_lengthsClip = new(serializedDiff, diffPos0, diffPos0 + out_diffHead.LengthSize, temp_cache, cacheSize);
            temp_cache += cacheSize;

            diffPos0 += out_diffHead.LengthSize;
            code_inc_newPosClip = new(serializedDiff, diffPos0, diffPos0 + out_diffHead.IncNewPosSize, temp_cache, cacheSize);
            temp_cache += cacheSize;

            diffPos0 += out_diffHead.IncNewPosSize;
            code_inc_oldPosClip = new(serializedDiff, diffPos0, diffPos0 + out_diffHead.IncOldPosSize, temp_cache, cacheSize);
        }

        // _covers_init(&self->base, out_diffHead->coverCount, &self->code_inc_oldPosClip, &self->code_inc_newPosClip, &self->code_lengthsClip, 0);
        coverCount = out_diffHead.CoverCount;
    }
}

public unsafe class HDiffHead
{
    public ulong CoverCount { get; private set; }

    public ulong LengthSize { get; private set; }

    public ulong IncNewPosSize { get; private set; }

    public ulong IncOldPosSize { get; private set; }

    public ulong NewDataDiffSize { get; private set; }

    public ulong HeadEndPos { get; private set; }

    public ulong CoverEndPos { get; private set; }

    // read_diff_head
    public bool ReadDiffHead(HPatchStreamInput serializedDiff)
    {
        ulong diffPos0;
        ulong diffPos_end = serializedDiff.StreamSize;
        byte* temp_cache = stackalloc byte[1024 * 4]; // TODO
        StreamCacheClip diffHeadClip = new(serializedDiff, 0, diffPos_end, temp_cache, (1024 * 4));

        ulong value;
        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        CoverCount = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        LengthSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        IncNewPosSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        IncOldPosSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        NewDataDiffSize = value;

        diffPos0 = diffHeadClip.ReadPosOfSrcStream();
        HeadEndPos = diffPos0;
        if (LengthSize > unchecked((ulong)(diffPos_end - diffPos0)))
        {
            return false;
        }

        diffPos0 += LengthSize;
        if (IncNewPosSize > unchecked((ulong)(diffPos_end - diffPos0)))
        {
            return false;
        }

        diffPos0 += IncNewPosSize;
        if (IncOldPosSize > unchecked((ulong)(diffPos_end - diffPos0)))
        {
            return false;
        }

        diffPos0 += IncOldPosSize;
        CoverEndPos = diffPos0;
        if (NewDataDiffSize > unchecked((ulong)(diffPos_end - diffPos0)))
        {
            return false;
        }

        return true;
    }
}

public partial struct _TCompressedCovers
{
    public Covers @base;

    public StreamCacheClip coverClip;

    public DecompressInputStream decompresser;
}

public unsafe partial struct _TArrayCovers
{
    public IHPatchCovers ICovers;

    public void* pCCovers;

    [NativeTypeName("hpatch_size_t")]
    public nuint coverCount;

    [NativeTypeName("hpatch_size_t")]
    public nuint cur_index;

    [NativeTypeName("hpatch_BOOL")]
    public int is32;
}

public partial struct hpatch_TCCover32
{
    [NativeTypeName("hpatch_uint32_t")]
    public uint oldPos;

    [NativeTypeName("hpatch_uint32_t")]
    public uint newPos;

    [NativeTypeName("hpatch_uint32_t")]
    public uint length;

    [NativeTypeName("hpatch_uint32_t")]
    public uint cachePos;
}

public partial struct hpatch_TCCover64
{
    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong oldPos;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong newPos;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong length;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong cachePos;
}

public unsafe partial struct _cache_old_TStreamInput
{
    public _TArrayCovers arrayCovers;

    [NativeTypeName("hpatch_BOOL")]
    public int isInHitCache;

    [NativeTypeName("hpatch_size_t")]
    public nuint maxCachedLen;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong readFromPos;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong readFromPosEnd;

    [NativeTypeName("const TByte *")]
    public byte* caches;

    [NativeTypeName("const TByte *")]
    public byte* cachesEnd;

    [NativeTypeName("const hpatch_TStreamInput *")]
    public HPatchStreamInput* oldData;
}

public unsafe partial struct rle0_decoder_t
{
    [NativeTypeName("const unsigned char *")]
    public byte* code;

    [NativeTypeName("const unsigned char *")]
    public byte* code_end;

    [NativeTypeName("hpatch_size_t")]
    public nuint len0;

    [NativeTypeName("hpatch_size_t")]
    public nuint lenv;

    [NativeTypeName("hpatch_BOOL")]
    public int isNeedDecode0;
}

public unsafe partial struct hpatch_TCoverList
{
    public IHPatchCovers* ICovers;

    [NativeTypeName("unsigned char[16384]")]
    public fixed byte _buf[16384];
}

public enum TByteRleType
{
    kByteRleType_rle0 = 0,
    kByteRleType_rle255 = 1,
    kByteRleType_rle = 2,
    kByteRleType_unrle = 3,
}

public unsafe class HDiffzHead
{
    public ulong CoverCount { get; set; }

    public ulong CoverBufSize { get; set; }

    public ulong CompressCoverBufSize { get; set; }

    public ulong RleCtrlBufSize { get; set; }

    public ulong CompressRleCtrlBufSize { get; set; }

    public ulong RleCodeBufSize { get; set; }

    public ulong CompressRleCodeBufSize { get; set; }

    public ulong NewDataDiffSize { get; set; }

    public ulong CompressNewDataDiffSize { get; set; }

    public ulong TypesEndPos { get; set; }

    public ulong CompressSizeBeginPos { get; set; }

    public ulong HeadEndPos { get; set; }

    public ulong CoverEndPos { get; set; }
}

public unsafe class StreamCacheClip
{
    public ulong StreamPos { get; private set; }
    public ulong StreamPosEnd { get; private set; }
    private HPatchStreamInput srcStream;
    private byte* cacheBuf;
    public nuint CacheBegin { get; private set; }

    public nuint CacheEnd { get; private set; }

    // _TStreamCacheClip_init
    public StreamCacheClip(HPatchStreamInput srcStream, ulong streamPos, ulong streamPos_end, byte* aCache, nuint cacheSize)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(streamPos, streamPos_end);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(streamPos_end, srcStream?.StreamSize ?? 0);

        StreamPos = streamPos;
        StreamPosEnd = streamPos_end;
        this.srcStream = srcStream;
        cacheBuf = aCache;
        CacheBegin = cacheSize;
        CacheEnd = cacheSize;
    }

    // _TStreamCacheClip_isFinish
    public bool IsFinish()
    {
        return 0 == LeaveSize();
    }

    // _TStreamCacheClip_isCacheEmpty
    public bool IsCacheEmpty()
    {
        return CacheBegin == CacheEnd;
    }

    // _TStreamCacheClip_cachedSize
    public nuint CachedSize()
    {
        return CacheEnd - CacheBegin;
    }

    // _TStreamCacheClip_leaveSize
    public nuint LeaveSize()
    {
        return unchecked((nuint)(StreamPosEnd - StreamPos)) + CachedSize();
    }

    // _TStreamCacheClip_readPosOfSrcStream
    public nuint ReadPosOfSrcStream()
    {
        return unchecked((nuint)StreamPos) - CachedSize();
    }

    // _TStreamCacheClip_readType_end
    public bool ReadTypeEnd(byte endTag, out string type)
    {
        byte* type_begin;
        nuint i;
        nuint readLen = 259 + 1;

        if (readLen > unchecked((ulong)(StreamPosEnd - StreamPos) + (ulong)((nuint)(CacheEnd - CacheBegin))))
        {
            readLen = (nuint)((ulong)(StreamPosEnd - StreamPos) + (ulong)((nuint)(CacheEnd - CacheBegin)));
        }

        type_begin = AccessData(readLen);
        if (type_begin == null)
        {
            type = default!;
            return false;
        }

        byte* outType = stackalloc byte[260];

        for (i = 0; i < readLen; ++i)
        {
            if (type_begin[i] != endTag)
            {
                continue;
            }
            else
            {
                Unsafe.CopyBlockUnaligned(outType, type_begin, unchecked((uint)i));
                // out_type[i] = (sbyte)('\0');
                type = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(outType));
                CacheBegin += i + 1;
                return true;
            }
        }

        type = default!;
        return false;
    }

    // _TStreamCacheClip_updateCache
    public bool UpdateCache()
    {
        byte* buf0 = &cacheBuf[0];
        ulong streamSize = StreamPosEnd - StreamPos;
        nuint readSize = CacheBegin;

        if (readSize > streamSize)
        {
            readSize = (nuint)(streamSize);
        }

        if (readSize == 0)
        {
            return true;
        }

        if (CacheBegin != CacheEnd)
        {
            Buffer.MemoryCopy(buf0 + CacheBegin, buf0 + (CacheBegin - readSize), CacheEnd - CacheBegin, CacheEnd - CacheBegin);
        }

        if (!srcStream.Read(StreamPos, buf0 + (CacheEnd - readSize), buf0 + CacheEnd))
        {
            return false;
        }

        CacheBegin -= readSize;
        StreamPos += readSize;
        return true;
    }

    // _TStreamCacheClip_skipData_noCheck
    public void SkipDataNoCheck(nuint skipSize)
    {
        CacheBegin += skipSize;
    }

    // _TStreamCacheClip_skipData
    public bool SkipData(ulong skipLongSize)
    {
        while (skipLongSize > 0)
        {
            nuint len = CacheEnd;

            if (len > skipLongSize)
            {
                len = (nuint)(skipLongSize);
            }

            if (AccessData(len) != null)
            {
                CacheBegin += len;
                skipLongSize -= len;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    // _TStreamCacheClip_unpackUIntWithTag
    public bool UnpackUIntWithTag(ulong* result, uint kTagBit)
    {
        byte* curCode, codeBegin;
        nuint readSize = ((8 * 8 + 6) / 7 + 1);
        ulong dataSize = ((ulong)(StreamPosEnd - StreamPos) + (ulong)((nuint)(CacheEnd - CacheBegin)));

        if (readSize > dataSize)
        {
            readSize = (nuint)(dataSize);
        }

        codeBegin = AccessData(readSize);
        if (codeBegin == null)
        {
            return false;
        }

        curCode = codeBegin;
        if (HPatch.hpatch_unpackUIntWithTag(unchecked((byte**)(&curCode)), codeBegin + readSize, result, kTagBit) == 0)
        {
            return false;
        }

        CacheBegin += (nuint)(curCode - codeBegin);
        return true;
    }

    // _TStreamCacheClip_readDataTo
    public bool ReadDataTo(byte* outBuf, byte* bufEnd)
    {
        nuint readLen = ((nuint)(CacheEnd - CacheBegin));
        nuint outLen = unchecked((nuint)(bufEnd - outBuf));

        if (unchecked(readLen) >= outLen)
        {
            readLen = outLen;
        }

        Unsafe.CopyBlockUnaligned(outBuf, &cacheBuf[CacheBegin], unchecked((uint)readLen));
        CacheBegin += readLen;
        outLen -= readLen;
        if ((outLen) != 0)
        {
            outBuf += readLen;
            if (outLen < (CacheEnd >> 1))
            {
                if (!UpdateCache())
                {
                    return false;
                }

                if (outLen > unchecked((nuint)(CacheEnd - CacheBegin)))
                {
                    return false;
                }

                return ReadDataTo(outBuf, bufEnd);
            }
            else
            {
                if (!srcStream.Read(StreamPos, outBuf, bufEnd))
                {
                    return false;
                }

                StreamPos += outLen;
            }
        }

        return true;
    }

    // _TStreamCacheClip_addDataTo
    public bool AddDataTo(byte* dst, nuint addLen)
    {
        byte* src = ReadData(addLen);

        if (src == null)
        {
            return false;
        }

        HPatch.addData(dst, src, addLen);
        return true;
    }

    // _TStreamCacheClip_readData
    public byte* ReadData([NativeTypeName("hpatch_size_t")] nuint readSize)
    {
        byte* result = AccessData(readSize);
        SkipDataNoCheck(readSize);
        return result;
    }

    // _TStreamCacheClip_accessData
    public byte* AccessData([NativeTypeName("hpatch_size_t")] nuint readSize)
    {
        if (readSize > unchecked((nuint)(CacheEnd - CacheBegin)))
        {
            if (!UpdateCache())
            {
                return null;
            }

            if (readSize > CachedSize())
            {
                return null;
            }
        }

        return &cacheBuf[CacheBegin];
    }
}

public unsafe class OutStreamCache
{
    private ulong writeToPos;
    private HPatchStreamOutput dstStream;
    private byte* cacheBuf;
    private nuint cacheCur;
    private nuint cacheEnd;

    // _TOutStreamCache_init
    public OutStreamCache(HPatchStreamOutput dstStream, byte* aCache, nuint aCacheSize)
    {
        writeToPos = 0;
        cacheCur = 0;
        this.dstStream = dstStream;
        cacheBuf = aCache;
        cacheEnd = aCacheSize;
    }

    // __TOutStreamCache_writeStream
    public bool WriteStream(byte* data, nuint dataSize)
    {
        if (!dstStream.Write(writeToPos, data, data + dataSize))
        {
            return false;
        }

        writeToPos += dataSize;
        return true;
    }

    // _TOutStreamCache_flush
    public bool Flush()
    {
        nuint curSize = cacheCur;

        if (curSize > 0)
        {
            if (!WriteStream(cacheBuf, curSize))
            {
                return false;
            }

            cacheCur = 0;
        }

        return true;
    }

    // _TOutStreamCache_write
    public bool Write(byte* data, nuint dataSize)
    {
        while (dataSize > 0)
        {
            nuint copyLen;
            nuint curSize = cacheCur;

            if ((dataSize >= cacheEnd) && (curSize == 0))
            {
                return WriteStream(data, dataSize);
            }

            copyLen = cacheEnd - curSize;
            copyLen = (copyLen <= dataSize) ? copyLen : dataSize;
            Unsafe.CopyBlockUnaligned(cacheBuf + curSize, data, unchecked((uint)copyLen));
            cacheCur = curSize + copyLen;
            data += copyLen;
            dataSize -= copyLen;
            if (cacheCur == cacheEnd)
            {
                if (!Flush())
                {
                    return false;
                }
            }
        }

        return true;
    }

    // _TOutStreamCache_fill
    public bool Fill(byte fillValue, ulong fillLength)
    {
        while (fillLength > 0)
        {
            nuint curSize = cacheCur;
            nuint runStep = cacheEnd - curSize;

            runStep = (runStep <= fillLength) ? runStep : (nuint)(fillLength);
            Unsafe.InitBlockUnaligned(cacheBuf + curSize, fillValue, unchecked((uint)runStep));
            cacheCur = curSize + runStep;
            fillLength -= runStep;
            if (cacheCur == cacheEnd)
            {
                if (!Flush())
                {
                    return false;
                }
            }
        }

        return true;
    }

    // _TOutStreamCache_copyFromStream
    public bool CopyFromStream(HPatchStreamInput src, ulong srcPos, ulong copyLength)
    {
        while (copyLength > 0)
        {
            nuint curSize = cacheCur;
            nuint runStep = cacheEnd - curSize;
            byte* buf = cacheBuf + curSize;

            runStep = (runStep <= copyLength) ? runStep : (nuint)(copyLength);
            if (!src.Read(srcPos, buf, buf + runStep))
            {
                return false;
            }

            srcPos += runStep;
            cacheCur = curSize + runStep;
            copyLength -= runStep;
            if (cacheCur == cacheEnd)
            {
                if (!Flush())
                {
                    return false;
                }
            }
        }

        return true;
    }

    // _TOutStreamCache_copyFromClip
    public bool CopyFromClip(StreamCacheClip src, ulong copyLength)
    {
        while (copyLength > 0)
        {
            byte* data;
            nuint runStep = (cacheEnd <= copyLength) ? cacheEnd : (nuint)(copyLength);

            data = src.ReadData(runStep);
            if (data == null)
            {
                return false;
            }

            if (!Write(data, unchecked(runStep)))
            {
                return false;
            }

            copyLength -= runStep;
        }

        return true;
    }

    // _TOutStreamCache_copyFromSelf
    public bool CopyFromSelf(ulong aheadLength, ulong copyLength)
    {
        HPatchStreamInput src = dstStream;
        ulong srcPos = writeToPos + cacheCur - aheadLength;

        // if (unchecked(src)->read == null)
        // {
        //     return 0;
        // }

        if (((aheadLength < 1) ? 1 : 0 | ((aheadLength > writeToPos + cacheCur) ? 1 : 0)) != 0)
        {
            return false;
        }

        if (srcPos + copyLength <= writeToPos)
        {
            return CopyFromStream(src, srcPos, copyLength);
        }

        if (srcPos >= writeToPos)
        {
            return CopyInMem(aheadLength, copyLength);
        }

        if (writeToPos + cacheCur <= srcPos + cacheEnd)
        {
            byte* dstBuf = cacheBuf + cacheCur;
            nuint runLen = (nuint)(writeToPos - srcPos);

            if (!src.Read(srcPos, dstBuf, dstBuf + runLen))
            {
                return false;
            }

            copyLength -= runLen;
            cacheCur += runLen;
            if (cacheCur == cacheEnd)
            {
                while (true)
                {
                    if (cacheCur == cacheEnd)
                    {
                        if (!Flush())
                        {
                            return false;
                        }
                    }

                    if (copyLength > 0)
                    {
                        runLen = (cacheEnd <= copyLength) ? cacheEnd : (nuint)(copyLength);
                        copyLength -= runLen;
                        cacheCur = runLen;
                    }
                    else
                    {
                        return true;
                    }
                }
            }

            return CopyInMem(aheadLength, copyLength);
        }

        return CopyFromStream(src, srcPos, copyLength);
    }

    // _patch_add_old_with_rle
    public bool PatchAddOldWithRle(BytesRleLoadStream rleLoader, HPatchStreamInput old, ulong oldPos, ulong addLength, byte* aCache, nuint aCacheSize)
    {
        while (addLength > 0)
        {
            nuint decodeStep = aCacheSize;

            if (decodeStep > addLength)
            {
                decodeStep = (nuint)(addLength);
            }

            if (!old.Read(oldPos, aCache, aCache + decodeStep))
            {
                return false;
            }

            if (!rleLoader.DecodeAdd(aCache, decodeStep))
            {
                return false;
            }

            if (!Write(aCache, decodeStep))
            {
                return false;
            }

            oldPos += decodeStep;
            addLength -= decodeStep;
        }

        return true;
    }

    // patchByClip
    public bool PatchByClip(HPatchStreamInput oldData, IHPatchCovers covers, StreamCacheClip code_newDataDiffClip, BytesRleLoadStream rle_loader, byte* temp_cache, nuint cache_size)
    {
        ulong newDataSize = LeaveSize();
        ulong oldDataSize = oldData.StreamSize;
        ulong coverCount = covers.LeaveCoverCount();
        ulong newPosBack = 0;

        ArgumentOutOfRangeException.ThrowIfLessThan<nuint>(cache_size, (sizeof(ulong) * 8 + 6) / 7 + 1);
        while ((coverCount--) != 0)
        {
            if (!covers.ReadCover(out HPatchCover? cover))
            {
                return false;
            }

            if (cover.NewPos < newPosBack)
            {
                return false;
            }

            if (cover.Length > unchecked((ulong)(newDataSize - cover.NewPos)))
            {
                return false;
            }

            if (cover.OldPos > oldDataSize)
            {
                return false;
            }

            if (cover.Length > unchecked((ulong)(oldDataSize - cover.OldPos)))
            {
                return false;
            }

            if (newPosBack < cover.NewPos)
            {
                ulong copyLength = cover.NewPos - newPosBack;

                if (!CopyFromClip(code_newDataDiffClip, copyLength))
                {
                    return false;
                }

                if (!rle_loader.DecodeSkip(copyLength))
                {
                    return false;
                }
            }

            if (!PatchAddOldWithRle(rle_loader, oldData, cover.OldPos, cover.Length, temp_cache, cache_size))
            {
                return false;
            }

            newPosBack = cover.NewPos + cover.Length;
        }

        if (newPosBack < newDataSize)
        {
            ulong copyLength = newDataSize - newPosBack;

            if (!CopyFromClip(code_newDataDiffClip, copyLength))
            {
                return false;
            }

            if (!rle_loader.DecodeSkip(copyLength))
            {
                return false;
            }

            newPosBack = newDataSize;
        }

        if (!Flush())
        {
            return false;
        }

        return rle_loader.IsFinish()
            && covers.IsFinish()
            && IsFinish()
            && code_newDataDiffClip.IsFinish()
            && (newPosBack == newDataSize);
    }

    // _TOutStreamCache_leaveSize
    public ulong LeaveSize()
    {
        return dstStream.StreamSize - writeToPos;
    }

    // _TOutStreamCache_isFinish
    public bool IsFinish()
    {
        return writeToPos == dstStream.StreamSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private bool CopyInMem(ulong aheadLength, ulong copyLength)
    {
        while (copyLength > 0)
        {
            byte* dstBuf = cacheBuf + cacheCur;
            byte* srcBuf = dstBuf - (nuint)(aheadLength);
            nuint runLen = (cacheCur + copyLength <= cacheEnd) ? (nuint)(copyLength) : (cacheEnd - cacheCur);
            nuint i;

            for (i = 0; i < unchecked(runLen); i++)
            {
                dstBuf[i] = srcBuf[i];
            }

            copyLength -= runLen;
            cacheCur += runLen;
            if (cacheCur == cacheEnd)
            {
                if (!Flush())
                {
                    return false;
                }

                runLen = (nuint)((aheadLength <= copyLength) ? aheadLength : copyLength);
                Buffer.MemoryCopy(cacheBuf + cacheEnd - (nuint)(aheadLength), cacheBuf, runLen, runLen);
                cacheCur = runLen;
                copyLength -= runLen;
            }
            else
            {
                if (copyLength != 0)
                {
                    throw new InvalidOperationException();
                }
            }
        }

        return true;
    }
}

public unsafe class DecompressInputStream : HPatchStreamInput
{
    private HPatchStreamInput IInputStream;
    private HPatchDecompress decompressPlugin;
    private void* decompressHandle;

    // _decompress_read
    public override bool Read(ulong readFromPos, byte* outData, byte* outDataEnd)
    {
        return decompressPlugin.decompress_part(decompressHandle, outData, outDataEnd);
    }
}

public unsafe partial struct TDiffToSingleStream
{
    public HPatchStreamInput @base;

    [NativeTypeName("const hpatch_TStreamInput *")]
    public HPatchStreamInput* diffStream;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong readedSize;

    [NativeTypeName("hpatch_size_t")]
    public nuint cachedBufBegin;

    [NativeTypeName("hpatch_BOOL")]
    public int isInSingleStream;

    [NativeTypeName("unsigned char[4096]")]
    public fixed byte buf[4096];
}

public unsafe class HPatchStreamInput
{
    protected void* StreamImport { get; set; }

    public ulong StreamSize { get; protected set; }

    protected HPatchStreamInput()
    {
    }

    // mem_as_hStreamInput
    public HPatchStreamInput(byte* mem, byte* memEnd)
    {
        StreamImport = mem;
        StreamSize = unchecked((ulong)(memEnd - mem));
    }

    // _read_mem_stream
    public virtual bool Read(ulong readFromPos, byte* outData, byte* outDataEnd)
    {
        byte* src = (byte*)(StreamImport);
        nuint readLen = unchecked((nuint)(outDataEnd - outData));

        if (readFromPos > StreamSize)
        {
            return false;
        }

        if (readLen > unchecked(StreamSize - readFromPos))
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(outData, src + readFromPos, unchecked((uint)readLen));
        return true;
    }
}

public unsafe class HPatchStreamOutput : HPatchStreamInput
{
    protected HPatchStreamOutput()
    {
    }

    // mem_as_hStreamOutput
    public HPatchStreamOutput(byte* mem, byte* memEnd)
        : base (mem, memEnd)
    {
    }

    // _write_mem_stream
    public virtual bool Write(ulong writeToPos, byte* data, byte* dataEnd)
    {
        byte* outDst = (byte*)(StreamImport);
        nuint writeLen = unchecked((nuint)(dataEnd - data));

        if (writeToPos > StreamSize)
        {
            return false;
        }

        if (writeLen > unchecked(StreamSize - writeToPos))
        {
            return false;
        }

        Unsafe.CopyBlockUnaligned(outDst + writeToPos, data, unchecked((uint)writeLen));
        return true;
    }

    // _patch_stream_with_cache
    public bool PatchStreamWithCache(HPatchStreamInput oldData, HPatchStreamInput serializedDiff, IHPatchCovers? cached_covers, byte* temp_cache, byte* temp_cache_end)
    {
        HDiffHead diffHead = new();
        StreamCacheClip code_newDataDiffClip;
        BytesRleLoadStream rle_loader;
        IHPatchCovers pcovers;
        ulong diffPos0;
        ulong diffPos_end = serializedDiff.StreamSize;
        nuint cacheSize = unchecked((nuint)((temp_cache_end - temp_cache) / (cached_covers != null ? (8 - 3) : 8)));

        ArgumentNullException.ThrowIfNull(oldData);
        ArgumentNullException.ThrowIfNull(serializedDiff);

        if (cached_covers == null)
        {
            PackedCovers packedCovers;
            try
            {
                packedCovers = new(diffHead, serializedDiff, temp_cache + cacheSize * (8 - 3), temp_cache_end);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            pcovers = packedCovers;
        }
        else
        {
            pcovers = cached_covers;
            if (!diffHead.ReadDiffHead(serializedDiff))
            {
                return false;
            }
        }

        diffPos0 = diffHead.CoverEndPos;
        code_newDataDiffClip = new(serializedDiff, diffPos0, diffPos0 + diffHead.NewDataDiffSize, temp_cache, cacheSize);
        temp_cache += cacheSize;
        diffPos0 += diffHead.NewDataDiffSize;

        {
            ulong rleCtrlSize;
            ulong rlePos0;
            StreamCacheClip rleHeadClip; // = &rle_loader.ctrlClip;

            if (cacheSize < unchecked((sizeof(ulong) * 8 + 6) / 7 + 1))
            {
                return false;
            }

            rleHeadClip = new(serializedDiff, diffPos0, diffPos_end, temp_cache, ((sizeof(ulong) * 8 + 6) / 7 + 1));

            if (!rleHeadClip.UnpackUIntWithTag(&rleCtrlSize, 0))
            {
                return false;
            }

            rlePos0 = (ulong)((rleHeadClip.StreamPos - ((nuint)(rleHeadClip.CacheEnd - rleHeadClip.CacheBegin))));
            if (rleCtrlSize > unchecked((ulong)(diffPos_end - rlePos0)))
            {
                return false;
            }

            StreamCacheClip ctrlClip = new(serializedDiff, rlePos0, rlePos0 + rleCtrlSize, temp_cache, cacheSize);
            temp_cache += cacheSize;
            StreamCacheClip rleCodeClip = new(serializedDiff, rlePos0 + rleCtrlSize, diffPos_end, temp_cache, cacheSize);
            temp_cache += cacheSize;
            rle_loader = new(ctrlClip, rleCodeClip);
        }

        {
            OutStreamCache outCache = new(this, temp_cache, cacheSize);
            temp_cache += cacheSize;
            return outCache.PatchByClip(oldData, pcovers, code_newDataDiffClip, rle_loader, temp_cache, cacheSize);
        }
    }
}

public unsafe class HPatchCompressedDiffInfo
{
    private ulong newDataSize;
    private ulong oldDataSize;
    private uint compressedCount;
    private string compressType;

    // getCompressedDiffInfo
    public HPatchCompressedDiffInfo(HPatchStreamInput compressedDiff)
    {
        HDiffzHead head = new HDiffzHead();

        ArgumentNullException.ThrowIfNull(compressedDiff);
        if (!ReadDiffzHead(head, compressedDiff))
        {
            throw new InvalidOperationException();
        }
    }

    // read_diffz_head
    [MemberNotNull(nameof(compressType))]
    public bool ReadDiffzHead(HDiffzHead out_head, HPatchStreamInput compressedDiff)
    {
        StreamCacheClip diffHeadClip;
        byte* temp_cache = stackalloc byte [1024 * 4]; // TODO

        diffHeadClip = new(compressedDiff, 0, compressedDiff.StreamSize, temp_cache, (1024 * 4));

        {
            if (!diffHeadClip.ReadTypeEnd("&"u8[0], out compressType))
            {
                return false;
            }

            if (!string.Equals(compressType, "HDIFF13", StringComparison.Ordinal))
            {
                return false;
            }
        }


        {
            if (!diffHeadClip.ReadTypeEnd("\0"u8[0], out compressType))
            {
                return false;
            }

            out_head.TypesEndPos = (diffHeadClip.StreamPos - ((nuint)((diffHeadClip).CacheEnd - (diffHeadClip).CacheBegin)));
        }

        ulong value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        newDataSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        oldDataSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CoverCount = value;

        out_head.CompressSizeBeginPos = ((diffHeadClip).StreamPos - ((nuint)((diffHeadClip).CacheEnd - (diffHeadClip).CacheBegin)));

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CoverBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CompressCoverBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.RleCtrlBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CompressRleCtrlBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.RleCodeBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CompressRleCodeBufSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.NewDataDiffSize = value;

        if (!diffHeadClip.UnpackUIntWithTag(&value, 0))
        {
            return false;
        }

        out_head.CompressNewDataDiffSize = value;

        out_head.HeadEndPos = ((diffHeadClip).StreamPos - ((nuint)((diffHeadClip).CacheEnd - (diffHeadClip).CacheBegin)));
        compressedCount = ((out_head.CompressCoverBufSize) != 0 ? 1U : 0U) + ((out_head.CompressRleCtrlBufSize) != 0 ? 1U : 0U) + ((out_head.CompressRleCodeBufSize) != 0 ? 1U : 0U) + ((out_head.CompressNewDataDiffSize) != 0 ? 1U : 0U);
        if (out_head.CompressCoverBufSize > 0)
        {
            out_head.CoverEndPos = out_head.HeadEndPos + out_head.CompressCoverBufSize;
        }
        else
        {
            out_head.CoverEndPos = out_head.HeadEndPos + out_head.CoverBufSize;
        }

        return true;
    }
}

public enum hpatch_dec_error_t
{
    hpatch_dec_ok = 0,
    hpatch_dec_mem_error,
    hpatch_dec_open_error,
    hpatch_dec_error,
    hpatch_dec_close_error,
}

public unsafe class HPatchDecompress
{
    [NativeTypeName("hpatch_BOOL (*)(const char *)")]
    public delegate*<sbyte*, int> is_can_open;

    [NativeTypeName("hpatch_decompressHandle (*)(struct hpatch_TDecompress *, hpatch_StreamPos_t, const struct hpatch_TStreamInput *, hpatch_StreamPos_t, hpatch_StreamPos_t)")]
    public delegate*<ulong, HPatchStreamInput, ulong, ulong, void*> open;

    [NativeTypeName("hpatch_BOOL (*)(struct hpatch_TDecompress *, hpatch_decompressHandle)")]
    public delegate*<void*, bool> close;

    [NativeTypeName("hpatch_BOOL (*)(hpatch_decompressHandle, unsigned char *, unsigned char *)")]
    public delegate*<void*, byte*, byte*, bool> decompress_part;

    [NativeTypeName("hpatch_BOOL (*)(hpatch_decompressHandle, hpatch_StreamPos_t, const struct hpatch_TStreamInput *, hpatch_StreamPos_t, hpatch_StreamPos_t)")]
    public delegate*<void*, ulong, HPatchStreamInput*, ulong, ulong, int> reset_code;

    [NativeTypeName("volatile hpatch_dec_error_t")]
    public hpatch_dec_error_t decError;

    // hpatch_deccompress_mem
    public bool hpatch_deccompress_mem(byte* code, byte* codeEnd, byte* outData, byte* outDataEnd)
    {
        void* dec = null;
        HPatchStreamInput codeStream = new(code, codeEnd);

        dec = open(unchecked((ulong)(outDataEnd - outData)), codeStream, 0, codeStream.StreamSize);
        if (dec == null)
        {
            return false;
        }

        bool result = decompress_part(dec, outData, outDataEnd);
        if (!close(dec))
        {
            throw new InvalidOperationException();
        }

        return result;
    }
}

public unsafe class StreamInputClip : HPatchStreamInput
{
    private readonly HPatchStreamInput srcStream;
    private readonly ulong clipBeginPos;

    // TStreamInputClip_init
    public StreamInputClip(HPatchStreamInput srcStream, ulong clipBeginPos, ulong clipEndPos)
    {
        ArgumentNullException.ThrowIfNull(srcStream);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clipBeginPos, clipEndPos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clipEndPos, srcStream.StreamSize);

        this.srcStream = srcStream;
        this.clipBeginPos = clipBeginPos;
        // self->base.streamImport = self;
        StreamSize = clipEndPos - clipBeginPos;
    }

    // _TStreamInputClip_read
    public override bool Read(ulong readFromPos, byte* outData, byte* outDataEnd)
    {
        if (readFromPos + unchecked((ulong)(outDataEnd - outData)) > StreamSize)
        {
            return false;
        }

        return srcStream.Read(readFromPos + clipBeginPos, outData, outDataEnd);
    }
}

public unsafe class StreamOutputClip : HPatchStreamOutput
{
    private HPatchStreamOutput srcStream;
    public ulong clipBeginPos;

    // TStreamOutputClip_init
    public StreamOutputClip(HPatchStreamOutput srcStream, ulong clipBeginPos, ulong clipEndPos)
    {
        ArgumentNullException.ThrowIfNull(srcStream);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clipBeginPos, clipEndPos);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clipEndPos, srcStream.StreamSize);

        this.srcStream = srcStream;
        this.clipBeginPos = clipBeginPos;
        // self->base.streamImport = self;
        StreamSize = clipEndPos - clipBeginPos;
    }

    // _TStreamOutputClip_write
    public override bool Write(ulong writePos, byte* data, byte* dataEnd)
    {
        if (writePos + unchecked((ulong)(dataEnd - data)) > StreamSize)
        {
            return false;
        }

        return srcStream.Write(writePos + clipBeginPos, data, dataEnd);
    }
}

public partial class HPatchCover
{
    public ulong OldPos { get; private set; }

    public ulong NewPos { get; private set; }

    public ulong Length { get; private set; }

    public HPatchCover()
    {
    }

    public HPatchCover(ulong oldPos, ulong newPos, ulong length)
    {
        this.OldPos = oldPos;
        this.NewPos = newPos;
        this.Length = length;
    }
}

public partial struct hpatch_TCover32
{
    [NativeTypeName("hpatch_uint32_t")]
    public uint oldPos;

    [NativeTypeName("hpatch_uint32_t")]
    public uint newPos;

    [NativeTypeName("hpatch_uint32_t")]
    public uint length;
}

public partial struct hpatch_TCover_sz
{
    [NativeTypeName("size_t")]
    public nuint oldPos;

    [NativeTypeName("size_t")]
    public nuint newPos;

    [NativeTypeName("size_t")]
    public nuint length;
}

public interface IHPatchCovers
{
    ulong LeaveCoverCount();

    bool ReadCover([NotNullWhen(true)] out HPatchCover? cover);

    bool IsFinish();

    // _covers_close_nil
    public bool Close()
    {
        return true;
    }
}

public unsafe partial struct hpatch_TOutputCovers
{
    [NativeTypeName("hpatch_BOOL (*)(struct hpatch_TOutputCovers *, const hpatch_TCover *)")]
    public delegate*<hpatch_TOutputCovers*, HPatchCover*, int> push_cover;

    [NativeTypeName("void (*)(struct hpatch_TOutputCovers *)")]
    public delegate*<hpatch_TOutputCovers*, void> collate_covers;
}

public unsafe partial struct hpatch_singleCompressedDiffInfo
{
    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong newDataSize;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong oldDataSize;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong uncompressedSize;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong compressedSize;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong diffDataPos;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong coverCount;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong stepMemSize;

    [NativeTypeName("char[260]")]
    public fixed sbyte compressType[260];
}

public unsafe partial struct sspatch_listener_t
{
    public void* import;

    [NativeTypeName("hpatch_BOOL (*)(struct sspatch_listener_t *, const hpatch_singleCompressedDiffInfo *, hpatch_TDecompress **, unsigned char **, unsigned char **)")]
    public delegate*<sspatch_listener_t*, hpatch_singleCompressedDiffInfo*, HPatchDecompress**, byte**, byte**, int> onDiffInfo;

    [NativeTypeName("void (*)(struct sspatch_listener_t *, unsigned char *, unsigned char *)")]
    public delegate*<sspatch_listener_t*, byte*, byte*, void> onPatchFinish;
}

public unsafe partial struct hpatch_TUncompresser_t
{
    public HPatchStreamInput @base;

    public HPatchDecompress* _decompressPlugin;

    [NativeTypeName("hpatch_decompressHandle")]
    public void* _decompressHandle;
}

public unsafe partial struct sspatch_coversListener_t
{
    public void* import;

    [NativeTypeName("void (*)(struct sspatch_coversListener_t *, hpatch_StreamPos_t)")]
    public delegate*<sspatch_coversListener_t*, ulong, void> onStepCoversReset;

    [NativeTypeName("void (*)(struct sspatch_coversListener_t *, const unsigned char *, const unsigned char *)")]
    public delegate*<sspatch_coversListener_t*, byte*, byte*, void> onStepCovers;
}

public unsafe partial struct sspatch_covers_t
{
    [NativeTypeName("const unsigned char *")]
    public byte* covers_cache;

    [NativeTypeName("const unsigned char *")]
    public byte* covers_cacheEnd;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong lastOldEnd;

    [NativeTypeName("hpatch_StreamPos_t")]
    public ulong lastNewEnd;

    public HPatchCover cover;
}

public static unsafe partial class HPatch
{
    [return: NativeTypeName("hpatch_BOOL")]
    public static int hpatch_packUIntWithTag([NativeTypeName("TByte **")] byte** out_code, [NativeTypeName("TByte *")] byte* out_code_end, [NativeTypeName("hpatch_StreamPos_t")] ulong uValue, [NativeTypeName("hpatch_uint")] uint highTag, [NativeTypeName("const hpatch_uint")] uint kTagBit)
    {
        byte* pcode = *out_code;
        ulong kMaxValueWithTag = ((ulong)(1) << (7 - kTagBit)) - 1;
        byte[] codeBuf;
        byte* codeEnd = codeBuf;

        while (uValue > unchecked(kMaxValueWithTag))
        {
            *codeEnd = uValue & ((1 << 7) - 1);
            ++codeEnd;
            uValue >>= 7;
        }

        if ((out_code_end - pcode) < (1 + (codeEnd - codeBuf)))
        {
            return 0;
        }

        *pcode = (byte)((byte)(uValue) | (highTag << (8 - kTagBit)) | (((codeBuf != codeEnd) ? 1 : 0) << (7 - kTagBit)));
        ++pcode;
        while (codeBuf != codeEnd)
        {
            --codeEnd;
            *pcode = (*codeEnd) | (byte)(((codeBuf != codeEnd) ? 1 : 0) << 7);
            ++pcode;
        }

        *out_code = pcode;
        return true;
    }

    [return: NativeTypeName("hpatch_uint")]
    public static uint hpatch_packUIntWithTag_size([NativeTypeName("hpatch_StreamPos_t")] ulong uValue, [NativeTypeName("const hpatch_uint")] uint kTagBit)
    {
        ulong kMaxValueWithTag = ((ulong)(1) << (7 - kTagBit)) - 1;
        uint size = 0;

        while (uValue > unchecked(kMaxValueWithTag))
        {
            ++size;
            uValue >>= 7;
        }

        ++size;
        return size;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int hpatch_unpackUIntWithTag([NativeTypeName("const TByte **")] byte** src_code, [NativeTypeName("const TByte *")] byte* src_code_end, [NativeTypeName("hpatch_StreamPos_t *")] ulong* result, [NativeTypeName("const hpatch_uint")] uint kTagBit)
    {
        ulong value;
        byte code;
        byte* pcode = *src_code;

        if (src_code_end <= pcode)
        {
            return 0;
        }

        code = *pcode;
        ++pcode;
        value = code & ((1 << (7 - kTagBit)) - 1);
        if ((code & (1 << (7 - kTagBit))) != 0)
        {
            do
            {
                if (unchecked(value >> (sizeof(ulong) * 8 - 7)) != 0)
                {
                    return 0;
                }

                if (src_code_end == pcode)
                {
                    return 0;
                }

                code = *pcode;
                ++pcode;
                value = (value << 7) | (code & ((1 << 7) - 1));
            }
            while ((code & (1 << 7)) != 0);

        }

        (*src_code) = pcode;
        *result = value;
        return true;
    }

    [NativeTypeName("const hpatch_uint")]
    public const uint kSignTagBit = 1;

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _bytesRle_load([NativeTypeName("TByte *")] byte* out_data, [NativeTypeName("TByte *")] byte* out_dataEnd, [NativeTypeName("const TByte *")] byte* rle_code, [NativeTypeName("const TByte *")] byte* rle_code_end)
    {
        byte* ctrlBuf, ctrlBuf_end;
        nuint ctrlSize;

        do
        {
            if (_unpackUIntWithTag(&rle_code, rle_code_end, &ctrlSize, 0) == 0)
            {
                return 0;
            }
        }
        while ((0) != 0);

        if (ctrlSize > unchecked((nuint)(rle_code_end - rle_code)))
        {
            return 0;
        }

        ctrlBuf = rle_code;
        rle_code += ctrlSize;
        ctrlBuf_end = rle_code;
        while (ctrlBuf_end - ctrlBuf > 0)
        {
            TByteRleType type = (TByteRleType)((*ctrlBuf) >> (8 - kByteRleType_bit));
            nuint length;

            do
            {
                if (_unpackUIntWithTag(&ctrlBuf, ctrlBuf_end, &length, kByteRleType_bit) == 0)
                {
                    return 0;
                }
            }
            while ((0) != 0);

            if (length >= unchecked((nuint)(out_dataEnd - out_data)))
            {
                return 0;
            }

            ++length;
            switch (type)
            {
                case kByteRleType_rle0:
                {
                    Unsafe.InitBlockUnaligned(out_data, 0, length);
                    out_data += length;
                }

                break;
                case kByteRleType_rle255:
                {
                    Unsafe.InitBlockUnaligned(out_data, 255, length);
                    out_data += length;
                }

                break;
                case kByteRleType_rle:
                {
                    if (1 > unchecked((nuint)(rle_code_end - rle_code)))
                    {
                        return 0;
                    }

                    Unsafe.InitBlockUnaligned(out_data, *rle_code, length);
                    ++rle_code;
                    out_data += length;
                }

                break;
                case kByteRleType_unrle:
                {
                    if (length > unchecked((nuint)(rle_code_end - rle_code)))
                    {
                        return 0;
                    }

                    Unsafe.CopyBlockUnaligned(out_data, rle_code, length);
                    rle_code += length;
                    out_data += length;
                }

                break;
            }
        }

        if ((ctrlBuf == ctrlBuf_end) && (rle_code == rle_code_end) && (out_data == out_dataEnd))
        {
            return true;
        }
        else
        {
            return 0;
        }
    }

    public static void addData([NativeTypeName("TByte *")] byte* dst, [NativeTypeName("const TByte *")] byte* src, [NativeTypeName("hpatch_size_t")] nuint length)
    {
        while ((length--) != 0)
        {
            *dst++ += *src++;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _unpackUIntWithTag([NativeTypeName("const TByte **")] byte** src_code, [NativeTypeName("const TByte *")] byte* src_code_end, [NativeTypeName("hpatch_size_t *")] nuint* result, [NativeTypeName("const hpatch_uint")] uint kTagBit)
    {
        if (unchecked(sizeof(nuint)) == unchecked(sizeof(ulong)))
        {
            return hpatch_unpackUIntWithTag(src_code, src_code_end, unchecked((ulong*)(result)), kTagBit);
        }
        else
        {
            ulong u64 = 0;
            int rt = hpatch_unpackUIntWithTag(src_code, src_code_end, &u64, kTagBit);
            nuint u = (nuint)(u64);

            *result = u;
            return rt & unchecked(u == u64) ? 1 : 0;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch([NativeTypeName("TByte *")] byte* out_newData, [NativeTypeName("TByte *")] byte* out_newData_end, [NativeTypeName("const TByte *")] byte* oldData, [NativeTypeName("const TByte *")] byte* oldData_end, [NativeTypeName("const TByte *")] byte* serializedDiff, [NativeTypeName("const TByte *")] byte* serializedDiff_end)
    {
        byte* code_lengths, code_lengths_end, code_inc_newPos, code_inc_newPos_end, code_inc_oldPos, code_inc_oldPos_end, code_newDataDiff, code_newDataDiff_end;
        nuint coverCount;

        ((void)((!!(out_newData <= out_newData_end)) || (_wassert("out_newData<=out_newData_end", "patch.c", unchecked((uint)(234))) , 0) != 0));
        ((void)((!!(oldData <= oldData_end)) || (_wassert("oldData<=oldData_end", "patch.c", unchecked((uint)(235))) , 0) != 0));
        ((void)((!!(serializedDiff <= serializedDiff_end)) || (_wassert("serializedDiff<=serializedDiff_end", "patch.c", unchecked((uint)(236))) , 0) != 0));
        do
        {
            if (_unpackUIntWithTag(&serializedDiff, serializedDiff_end, &coverCount, 0) == 0)
            {
                return 0;
            }
        }
        while ((0) != 0);


        {
            nuint lengthSize, inc_newPosSize, inc_oldPosSize, newDataDiffSize;

            do
            {
                if (_unpackUIntWithTag(&serializedDiff, serializedDiff_end, &lengthSize, 0) == 0)
                {
                    return 0;
                }
            }
            while ((0) != 0);

            do
            {
                if (_unpackUIntWithTag(&serializedDiff, serializedDiff_end, &inc_newPosSize, 0) == 0)
                {
                    return 0;
                }
            }
            while ((0) != 0);

            do
            {
                if (_unpackUIntWithTag(&serializedDiff, serializedDiff_end, &inc_oldPosSize, 0) == 0)
                {
                    return 0;
                }
            }
            while ((0) != 0);

            do
            {
                if (_unpackUIntWithTag(&serializedDiff, serializedDiff_end, &newDataDiffSize, 0) == 0)
                {
                    return 0;
                }
            }
            while ((0) != 0);

            if (lengthSize > unchecked((nuint)(serializedDiff_end - serializedDiff)))
            {
                return 0;
            }

            code_lengths = serializedDiff;
            serializedDiff += lengthSize;
            code_lengths_end = serializedDiff;
            if (inc_newPosSize > unchecked((nuint)(serializedDiff_end - serializedDiff)))
            {
                return 0;
            }

            code_inc_newPos = serializedDiff;
            serializedDiff += inc_newPosSize;
            code_inc_newPos_end = serializedDiff;
            if (inc_oldPosSize > unchecked((nuint)(serializedDiff_end - serializedDiff)))
            {
                return 0;
            }

            code_inc_oldPos = serializedDiff;
            serializedDiff += inc_oldPosSize;
            code_inc_oldPos_end = serializedDiff;
            if (newDataDiffSize > unchecked((nuint)(serializedDiff_end - serializedDiff)))
            {
                return 0;
            }

            code_newDataDiff = serializedDiff;
            serializedDiff += newDataDiffSize;
            code_newDataDiff_end = serializedDiff;
        }

        do
        {
            if (_bytesRle_load(out_newData, out_newData_end, serializedDiff, serializedDiff_end) == 0)
            {
                return 0;
            }
        }
        while ((0) != 0);


        {
            nuint newDataSize = (nuint)(out_newData_end - out_newData);
            nuint oldPosBack = 0;
            nuint newPosBack = 0;
            nuint i;

            for (i = 0; i < coverCount; ++i)
            {
                nuint copyLength, addLength, oldPos, inc_oldPos, inc_oldPos_sign;

                do
                {
                    if (_unpackUIntWithTag(&code_inc_newPos, code_inc_newPos_end, &copyLength, 0) == 0)
                    {
                        return 0;
                    }
                }
                while ((0) != 0);

                do
                {
                    if (_unpackUIntWithTag(&code_lengths, code_lengths_end, &addLength, 0) == 0)
                    {
                        return 0;
                    }
                }
                while ((0) != 0);

                if (code_inc_oldPos >= code_inc_oldPos_end)
                {
                    return 0;
                }

                inc_oldPos_sign = (*code_inc_oldPos) >> (8 - kSignTagBit);
                do
                {
                    if (_unpackUIntWithTag(&code_inc_oldPos, code_inc_oldPos_end, &inc_oldPos, kSignTagBit) == 0)
                    {
                        return 0;
                    }
                }
                while ((0) != 0);

                if (inc_oldPos_sign == 0)
                {
                    oldPos = oldPosBack + inc_oldPos;
                }
                else
                {
                    oldPos = oldPosBack - inc_oldPos;
                }

                if (copyLength > 0)
                {
                    if (copyLength > unchecked((nuint)(newDataSize - newPosBack)))
                    {
                        return 0;
                    }

                    if (copyLength > unchecked((nuint)(code_newDataDiff_end - code_newDataDiff)))
                    {
                        return 0;
                    }

                    Unsafe.CopyBlockUnaligned(out_newData + newPosBack, code_newDataDiff, copyLength);
                    code_newDataDiff += copyLength;
                    newPosBack += copyLength;
                }

                if ((unchecked(addLength > (nuint)(newDataSize - newPosBack))))
                {
                    return 0;
                }

                if (unchecked(oldPos > (nuint)(oldData_end - oldData)) || unchecked(addLength > (nuint)(oldData_end - oldData - oldPos)))
                {
                    return 0;
                }

                addData(out_newData + newPosBack, oldData + oldPos, addLength);
                oldPosBack = oldPos;
                newPosBack += addLength;
            }

            if (newPosBack < unchecked(newDataSize))
            {
                nuint copyLength = newDataSize - newPosBack;

                if (unchecked(copyLength) > unchecked((nuint)(code_newDataDiff_end - code_newDataDiff)))
                {
                    return 0;
                }

                Unsafe.CopyBlockUnaligned(out_newData + newPosBack, code_newDataDiff, copyLength);
                code_newDataDiff += copyLength;
            }
        }

        if ((code_lengths == code_lengths_end) && (code_inc_newPos == code_inc_newPos_end) && (code_inc_oldPos == code_inc_oldPos_end) && (code_newDataDiff == code_newDataDiff_end))
        {
            return true;
        }
        else
        {
            return 0;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int getStreamClip(StreamCacheClip* out_clip, DecompressInputStream* out_stream, [NativeTypeName("hpatch_StreamPos_t")] ulong dataSize, [NativeTypeName("hpatch_StreamPos_t")] ulong compressedSize, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* stream, [NativeTypeName("hpatch_StreamPos_t *")] ulong* pCurStreamPos, HPatchDecompress* decompressPlugin, [NativeTypeName("TByte *")] byte* aCache, [NativeTypeName("hpatch_size_t")] nuint cacheSize)
    {
        ulong curStreamPos = *pCurStreamPos;

        if (compressedSize == 0)
        {
            if ((curStreamPos + dataSize) < curStreamPos)
            {
                return 0;
            }

            if ((curStreamPos + dataSize) > stream->StreamSize)
            {
                return 0;
            }

            if ((out_clip) != null)
            {
                _TStreamCacheClip_init(out_clip, stream, curStreamPos, curStreamPos + dataSize, aCache, cacheSize);
            }

            curStreamPos += dataSize;
        }
        else
        {
            if ((curStreamPos + compressedSize) < curStreamPos)
            {
                return 0;
            }

            if ((curStreamPos + compressedSize) > stream->StreamSize)
            {
                return 0;
            }

            if ((out_clip) != null)
            {
                out_stream->IInputStream.StreamImport = out_stream;
                out_stream->IInputStream.StreamSize = dataSize;
                out_stream->IInputStream.read = _decompress_read;
                out_stream->decompressPlugin = decompressPlugin;
                if (out_stream->decompressHandle == null)
                {
                    out_stream->decompressHandle = decompressPlugin->open(decompressPlugin, dataSize, stream, curStreamPos, curStreamPos + compressedSize);
                    if (out_stream->decompressHandle == null)
                    {
                        return 0;
                    }
                }
                else
                {
                    if (decompressPlugin->reset_code == null)
                    {
                        return 0;
                    }

                    if (decompressPlugin->reset_code(out_stream->decompressHandle, dataSize, stream, curStreamPos, curStreamPos + compressedSize) == 0)
                    {
                        return 0;
                    }
                }

                _TStreamCacheClip_init(out_clip, &out_stream->IInputStream, 0, out_stream->IInputStream.StreamSize, aCache, cacheSize);
            }

            curStreamPos += compressedSize;
        }

        *pCurStreamPos = curStreamPos;
        return true;
    }

    public static void memSet_add([NativeTypeName("TByte *")] byte* dst, [NativeTypeName("const TByte")] byte src, [NativeTypeName("hpatch_size_t")] nuint length)
    {
        while ((length--) != 0)
        {
            (*dst++) += src;
        }
    }



    [return: NativeTypeName("hpatch_BOOL")]
    public static int _patch_decompress_cache([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, HPatchStreamInput* once_in_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedDiff, HPatchDecompress* decompressPlugin, IHPatchCovers* cached_covers, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end)
    {
        StreamCacheClip coverClip = new StreamCacheClip();
        StreamCacheClip code_newDataDiffClip = new StreamCacheClip();
        BytesRleLoadStream rle_loader = new BytesRleLoadStream();
        HDiffzHead head = new HDiffzHead();
        HPatchCompressedDiffInfo diffInfo = new HPatchCompressedDiffInfo();
        DecompressInputStream[] decompressers = new DecompressInputStream();
        nuint i;
        ulong coverCount;
        int result = true;
        ulong diffPos0 = 0;
        ulong diffPos_end = compressedDiff->StreamSize;
        nuint cacheSize = (temp_cache_end - temp_cache) / ((cached_covers) != null ? (6 - 1) : 6);

        if (cacheSize <= 259)
        {
            return 0;
        }

        ((void)((!!(out_newData != null)) || (_wassert("out_newData!=0", "patch.c", unchecked((uint)(1265))) , 0) != 0));
        ((void)((!!(out_newData->write != null)) || (_wassert("out_newData->write!=0", "patch.c", unchecked((uint)(1266))) , 0) != 0));
        ((void)((!!(oldData != null)) || (_wassert("oldData!=0", "patch.c", unchecked((uint)(1267))) , 0) != 0));
        ((void)((!!(oldData->read != null)) || (_wassert("oldData->read!=0", "patch.c", unchecked((uint)(1268))) , 0) != 0));
        ((void)((!!(compressedDiff != null)) || (_wassert("compressedDiff!=0", "patch.c", unchecked((uint)(1269))) , 0) != 0));
        ((void)((!!(compressedDiff->read != null)) || (_wassert("compressedDiff->read!=0", "patch.c", unchecked((uint)(1270))) , 0) != 0));

        {
            if (read_diffz_head(&diffInfo, &head, compressedDiff) == 0)
            {
                return 0;
            }

            if ((diffInfo.oldDataSize != oldData->StreamSize) || (diffInfo.newDataSize != out_newData->StreamSize))
            {
                return 0;
            }

            if ((decompressPlugin == null) && (diffInfo.compressedCount != 0))
            {
                return 0;
            }

            if ((decompressPlugin) != null && (diffInfo.compressedCount > 0))
            {
                if (decompressPlugin->is_can_open(diffInfo.compressType) == 0)
                {
                    return 0;
                }
            }

            diffPos0 = head.HeadEndPos;
        }

        for (i = 0; i < unchecked(sizeof(DecompressInputStream) / sizeof(DecompressInputStream)); ++i)
        {
            decompressers[i].decompressHandle = null;
        }

        _TBytesRle_load_stream_init(&rle_loader);
        if ((cached_covers) != null)
        {
            diffPos0 = head.CoverEndPos;
        }
        else
        {
            if (getStreamClip(&coverClip, &decompressers[0], head.CoverBufSize, head.CompressCoverBufSize, compressedDiff, &diffPos0, decompressPlugin, temp_cache + cacheSize * (6 - 1), cacheSize) == 0)
            {
                result = 0;
                goto clear;
            }

            ;
        }

        if (getStreamClip(&rle_loader.ctrlClip, &decompressers[1], head.RleCtrlBufSize, head.CompressRleCtrlBufSize, compressedDiff, &diffPos0, decompressPlugin, temp_cache, cacheSize) == 0)
        {
            result = 0;
            goto clear;
        }

        ;
        temp_cache += cacheSize;
        if (getStreamClip(&rle_loader.rleCodeClip, &decompressers[2], head.RleCodeBufSize, head.CompressRleCodeBufSize, compressedDiff, &diffPos0, decompressPlugin, temp_cache, cacheSize) == 0)
        {
            result = 0;
            goto clear;
        }

        ;
        temp_cache += cacheSize;
        if (getStreamClip(&code_newDataDiffClip, &decompressers[3], head.NewDataDiffSize, head.CompressNewDataDiffSize, compressedDiff, &diffPos0, decompressPlugin, temp_cache, cacheSize) == 0)
        {
            result = 0;
            goto clear;
        }

        ;
        temp_cache += cacheSize;
        if (diffPos0 != diffPos_end)
        {
            result = 0;
            goto clear;
        }

        ;
        coverCount = head.CoverCount;

        {
            Covers covers = new Covers();
            IHPatchCovers* pcovers = null;
            OutStreamCache outCache = new OutStreamCache();

            _TOutStreamCache_init(&outCache, out_newData, temp_cache, cacheSize);
            temp_cache += cacheSize;
            if ((cached_covers) != null)
            {
                pcovers = cached_covers;
            }
            else
            {
                _covers_init(&covers, coverCount, &coverClip, &coverClip, &coverClip, true);
                pcovers = &covers.ICovers;
            }

            result = patchByClip(&outCache, oldData, pcovers, &code_newDataDiffClip, &rle_loader, temp_cache, cacheSize);
        }

        clear:
        for (i = 0; i < unchecked(sizeof(DecompressInputStream) / sizeof(DecompressInputStream)); ++i)
        {
            if ((decompressers[i].decompressHandle) != null)
            {
                if (decompressPlugin->close(decompressPlugin, decompressers[i].decompressHandle) == 0)
                {
                    result = 0;
                }

                decompressers[i].decompressHandle = null;
            }
        }

        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _cache_load_all([NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* data, [NativeTypeName("TByte *")] byte* cache, [NativeTypeName("TByte *")] byte* cache_end)
    {
        (unchecked((void)((!!((nuint)(cache_end - cache) == data->StreamSize)) || (_wassert("(hpatch_size_t)(cache_end-cache)==data->streamSize", "patch.c", (uint)(1340)) , 0) != 0)));
        return data->read(data, 0, cache, cache_end);
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _compressedCovers_close(IHPatchCovers* covers)
    {
        int result = true;
        _TCompressedCovers* self = (_TCompressedCovers*)(covers);

        if ((unchecked(self)) != null)
        {
            if ((self->decompresser.decompressHandle) != null)
            {
                result = self->decompresser.decompressPlugin->close(self->decompresser.decompressPlugin, self->decompresser.decompressHandle);
                self->decompresser.decompressHandle = null;
            }
        }

        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _compressedCovers_open(_TCompressedCovers** out_self, HPatchCompressedDiffInfo* out_diffInfo, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedDiff, HPatchDecompress* decompressPlugin, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end)
    {
        HDiffzHead head = new HDiffzHead();
        ulong diffPos0 = 0;
        _TCompressedCovers* self = null;


        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (sizeof(_TCompressedCovers))))
            {
                return 0;
            }

            (self) = (_TCompressedCovers*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(self) + (nuint)unchecked(sizeof(_TCompressedCovers));
        }

        ;
        if (read_diffz_head(out_diffInfo, &head, compressedDiff) == 0)
        {
            return 0;
        }

        diffPos0 = head.HeadEndPos;
        if (head.CompressCoverBufSize > 0)
        {
            if (decompressPlugin == null)
            {
                return 0;
            }

            if (decompressPlugin->is_can_open(out_diffInfo->compressType) == 0)
            {
                return 0;
            }
        }

        _covers_init(&self->base, head.CoverCount, &self->coverClip, &self->coverClip, &self->coverClip, true);
        self->base.ICovers.close = _compressedCovers_close;
        decompresser = default;
        if (getStreamClip(&self->coverClip, &self->decompresser, head.CoverBufSize, head.CompressCoverBufSize, compressedDiff, &diffPos0, decompressPlugin, temp_cache, temp_cache_end - temp_cache) == 0)
        {
            return 0;
        }

        ;
        *out_self = self;
        return true;
    }

    [return: NativeTypeName("hpatch_StreamPos_t")]
    public static ulong arrayCovers_memSize([NativeTypeName("hpatch_StreamPos_t")] ulong coverCount, [NativeTypeName("hpatch_BOOL")] int is32)
    {
        return coverCount * ((is32) != 0 ? sizeof(hpatch_TCCover32) : sizeof(hpatch_TCCover64));
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _arrayCovers_is_finish([NativeTypeName("const hpatch_TCovers *")] IHPatchCovers* covers)
    {
        _TArrayCovers* self = (_TArrayCovers*)(covers);

        return (self->coverCount == self->cur_index) ? 1 : 0;
    }

    [return: NativeTypeName("hpatch_StreamPos_t")]
    public static ulong _arrayCovers_leaveCoverCount([NativeTypeName("const hpatch_TCovers *")] IHPatchCovers* covers)
    {
        _TArrayCovers* self = (_TArrayCovers*)(covers);

        return self->coverCount - self->cur_index;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _arrayCovers_read_cover([NativeTypeName("struct hpatch_TCovers *")] IHPatchCovers* covers, HPatchCover* out_cover)
    {
        _TArrayCovers* self = (_TArrayCovers*)(covers);
        nuint i = self->cur_index;

        if (i < self->coverCount)
        {
            if ((self->is32) != 0)
            {
                hpatch_TCCover32* pCover = ((hpatch_TCCover32*)(self->pCCovers)) + i;

                out_cover->OldPos = pCover->oldPos;
                out_cover->NewPos = pCover->newPos;
                out_cover->Length = pCover->length;
            }
            else
            {
                hpatch_TCCover64* pCover = ((hpatch_TCCover64*)(self->pCCovers)) + i;

                out_cover->OldPos = pCover->oldPos;
                out_cover->NewPos = pCover->newPos;
                out_cover->Length = pCover->length;
            }

            self->cur_index = i + 1;
            return true;
        }
        else
        {
            return 0;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _arrayCovers_load(_TArrayCovers** out_self, IHPatchCovers* src_covers, [NativeTypeName("hpatch_BOOL")] int isUsedCover32, [NativeTypeName("hpatch_BOOL *")] int* out_isReadError, [NativeTypeName("TByte **")] byte** ptemp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end)
    {
        byte* temp_cache = *ptemp_cache;
        ulong _coverCount = src_covers->leave_cover_count(src_covers);
        ulong memSize = arrayCovers_memSize(_coverCount, isUsedCover32);
        nuint i;
        void* pCovers;
        _TArrayCovers* self = null;
        nuint coverCount = (nuint)(_coverCount);

        *out_isReadError = 0;
        if (unchecked(coverCount) != _coverCount)
        {
            return 0;
        }


        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (sizeof(_TArrayCovers))))
            {
                return 0;
            }

            (self) = (_TArrayCovers*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(self) + (nuint)unchecked(sizeof(_TArrayCovers));
        }

        ;

        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (memSize)))
            {
                return 0;
            }

            (pCovers) = (void*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(pCovers) + (nuint)(memSize);
        }

        ;
        if ((isUsedCover32) != 0)
        {
            hpatch_TCCover32* pdst = (hpatch_TCCover32*)(pCovers);

            for (i = 0; i < unchecked(coverCount); ++i , ++pdst)
            {
                HPatchCover cover = new HPatchCover();

                if (src_covers->read_cover(src_covers, &cover) == 0)
                {
                    *out_isReadError = true;
                    return 0;
                }

                pdst->oldPos = (uint)(cover.OldPos);
                pdst->newPos = (uint)(cover.NewPos);
                pdst->length = (uint)(cover.Length);
            }
        }
        else
        {
            hpatch_TCCover64* pdst = (hpatch_TCCover64*)(pCovers);

            for (i = 0; i < unchecked(coverCount); ++i , ++pdst)
            {
                if (src_covers->read_cover(src_covers, unchecked((HPatchCover*)(pdst))) == 0)
                {
                    *out_isReadError = true;
                    return 0;
                }
            }
        }

        if (src_covers->is_finish(src_covers) == 0)
        {
            *out_isReadError = true;
            return 0;
        }

        self->pCCovers = pCovers;
        self->is32 = isUsedCover32;
        self->coverCount = coverCount;
        self->cur_index = 0;
        self->ICovers.close = _covers_close_nil;
        self->ICovers.is_finish = _arrayCovers_is_finish;
        self->ICovers.leave_cover_count = _arrayCovers_leaveCoverCount;
        self->ICovers.read_cover = _arrayCovers_read_cover;
        *out_self = self;
        *ptemp_cache = temp_cache;
        return true;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_old_32([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            uint x = ((uint*)(_x))[0];
            uint y = ((uint*)(_y))[0];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_old([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            ulong x = ((ulong*)(_x))[0];
            ulong y = ((ulong*)(_y))[0];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_new_32([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            uint x = ((uint*)(_x))[1];
            uint y = ((uint*)(_y))[1];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_new([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            ulong x = ((ulong*)(_x))[1];
            ulong y = ((ulong*)(_y))[1];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_len_32([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            uint x = ((uint*)(_x))[2];
            uint y = ((uint*)(_y))[2];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    [return: NativeTypeName("hpatch_int")]
    public static int _arrayCovers_comp_by_len([NativeTypeName("const void *")] void* _x, [NativeTypeName("const void *")] void* _y)
    {

        {
            ulong x = ((ulong*)(_x))[2];
            ulong y = ((ulong*)(_y))[2];

            return unchecked(x < y) ? (-1) : (unchecked(x > y) ? 1 : 0);
        }

        ;
    }

    public static void _arrayCovers_sort_by_old(_TArrayCovers* self)
    {
        if ((self->is32) != 0)
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover32), _arrayCovers_comp_by_old_32);
        }
        else
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover64), _arrayCovers_comp_by_old);
        }
    }

    public static void _arrayCovers_sort_by_new(_TArrayCovers* self)
    {
        if ((self->is32) != 0)
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover32), _arrayCovers_comp_by_new_32);
        }
        else
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover64), _arrayCovers_comp_by_new);
        }
    }

    public static void _arrayCovers_sort_by_len(_TArrayCovers* self)
    {
        if ((self->is32) != 0)
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover32), _arrayCovers_comp_by_len_32);
        }
        else
        {
            qsort(self->pCCovers, self->coverCount, sizeof(hpatch_TCCover64), _arrayCovers_comp_by_len);
        }
    }

    [return: NativeTypeName("hpatch_size_t")]
    public static nuint _getMaxCachedLen([NativeTypeName("const _TArrayCovers *")] _TArrayCovers* src_covers, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end, [NativeTypeName("TByte *")] byte* cache_buf_end)
    {
        nuint kMaxCachedLen = ~((nuint)(0));
        ulong mlen = 0;
        ulong sum = 0;
        nuint coverCount = src_covers->coverCount;
        nuint i;
        _TArrayCovers cur_covers = *src_covers;
        nuint cacheSize = temp_cache_end - temp_cache;
        ulong memSize = arrayCovers_memSize(src_covers->coverCount, src_covers->is32);


        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (memSize)))
            {
                return 0;
            }

            (cur_covers.pCCovers) = (void*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(cur_covers.pCCovers) + (nuint)(memSize);
        }

        ;
        Unsafe.CopyBlockUnaligned(cur_covers.pCCovers, src_covers->pCCovers, (nuint)(memSize));
        _arrayCovers_sort_by_len(&cur_covers);
        for (i = 0; i < coverCount; ++i)
        {
            mlen = (((&cur_covers)->is32) != 0 ? ((uint*)((&cur_covers)->pCCovers))[(i) * 4 + (2)] : ((ulong*)((&cur_covers)->pCCovers))[(i) * 4 + (2)]);
            sum += mlen;
            if (sum <= cacheSize)
            {
                continue;
            }
            else
            {
                --mlen;
                break;
            }
        }

        if (mlen > unchecked(kMaxCachedLen))
        {
            mlen = kMaxCachedLen;
        }

        return (nuint)(mlen);
    }

    [return: NativeTypeName("hpatch_size_t")]
    public static nuint _set_cache_pos(_TArrayCovers* covers, [NativeTypeName("hpatch_size_t")] nuint maxCachedLen, [NativeTypeName("hpatch_StreamPos_t *")] ulong* poldPosBegin, [NativeTypeName("hpatch_StreamPos_t *")] ulong* poldPosEnd)
    {
        nuint coverCount = covers->coverCount;
        nuint kMinCacheCoverCount = coverCount / 8 + 1;
        ulong oldPosBegin = (~(ulong)(0));
        ulong oldPosEnd = 0;
        nuint cacheCoverCount = 0;
        nuint sum = 0;
        nuint i;

        for (i = 0; i < coverCount; ++i)
        {
            ulong clen = (((covers)->is32) != 0 ? ((uint*)((covers)->pCCovers))[(i) * 4 + (2)] : ((ulong*)((covers)->pCCovers))[(i) * 4 + (2)]);

            if (unchecked(clen) <= maxCachedLen)
            {
                ulong oldPos;


                {
                    if (((covers)->is32) != 0)
                    {
                        ((uint*)((covers)->pCCovers))[(i) * 4 + (3)] = (uint)(sum);
                    }
                    else
                    {
                        ((ulong*)((covers)->pCCovers))[(i) * 4 + (3)] = (sum);
                    }
                }

                ;
                sum += (nuint)(clen);
                ++cacheCoverCount;
                oldPos = (((covers)->is32) != 0 ? ((uint*)((covers)->pCCovers))[(i) * 4 + (0)] : ((ulong*)((covers)->pCCovers))[(i) * 4 + (0)]);
                if (oldPos < unchecked(oldPosBegin))
                {
                    oldPosBegin = oldPos;
                }

                if (unchecked(oldPos + clen) > oldPosEnd)
                {
                    oldPosEnd = oldPos + clen;
                }
            }
        }

        if (cacheCoverCount < kMinCacheCoverCount)
        {
            return 0;
        }

        *poldPosBegin = oldPosBegin;
        *poldPosEnd = oldPosEnd;
        return sum;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _cache_old_load([NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("hpatch_StreamPos_t")] ulong oldPos, [NativeTypeName("hpatch_StreamPos_t")] ulong oldPosAllEnd, _TArrayCovers* arrayCovers, [NativeTypeName("hpatch_size_t")] nuint maxCachedLen, [NativeTypeName("hpatch_size_t")] nuint sumCacheLen, [NativeTypeName("TByte *")] byte* old_cache, [NativeTypeName("TByte *")] byte* old_cache_end, [NativeTypeName("TByte *")] byte* cache_buf_end)
    {
        nuint kMinSpaceLen = (1 << (20 + 2));
        nuint kAccessPageSize = 4096;
        int result = true;
        nuint cur_i = 0, i;
        nuint coverCount = arrayCovers->coverCount;
        byte* cache_buf = old_cache_end;

        (unchecked((void)((!!((nuint)(old_cache_end - old_cache) >= sumCacheLen)) || (_wassert("(hpatch_size_t)(old_cache_end-old_cache)>=sumCacheLen", "patch.c", (uint)(1634)) , 0) != 0)));
        if (unchecked((nuint)(cache_buf_end - cache_buf)) >= kAccessPageSize * 2)
        {
            cache_buf = (byte*)(((nuint)(((nuint)(cache_buf)) + ((kAccessPageSize) - 1))) & (~(nuint)((kAccessPageSize) - 1)));
            if (unchecked((nuint)(cache_buf_end - cache_buf)) >= (kMinSpaceLen >> 1))
            {
                cache_buf_end = cache_buf + (kMinSpaceLen >> 1);
            }
            else
            {
                cache_buf_end = (byte*)(((nuint)(cache_buf_end)) & (~(nuint)((kAccessPageSize) - 1)));
            }
        }

        oldPos = (((ulong)(oldPos)) & (~(ulong)((kAccessPageSize) - 1)));
        if (oldPos < kMinSpaceLen)
        {
            oldPos = 0;
        }

        _arrayCovers_sort_by_old(arrayCovers);
        while (((oldPos < oldPosAllEnd) ? 1 : 0 & (cur_i < coverCount) ? 1 : 0) != 0)
        {
            ulong oldPosEnd;
            nuint readLen = (cache_buf_end - cache_buf);

            if (readLen > (oldPosAllEnd - oldPos))
            {
                readLen = (nuint)(oldPosAllEnd - oldPos);
            }

            if (oldData->read(oldData, oldPos, cache_buf, cache_buf + readLen) == 0)
            {
                result = 0;
                break;
            }

            oldPosEnd = oldPos + readLen;
            for (i = cur_i; i < coverCount; ++i)
            {
                ulong ioldPos, ioldPosEnd;
                ulong ilen = (((arrayCovers)->is32) != 0 ? ((uint*)((arrayCovers)->pCCovers))[(i) * 4 + (2)] : ((ulong*)((arrayCovers)->pCCovers))[(i) * 4 + (2)]);

                if (unchecked(ilen) > maxCachedLen)
                {
                    if (i == cur_i)
                    {
                        ++cur_i;
                    }

                    continue;
                }

                ioldPos = (((arrayCovers)->is32) != 0 ? ((uint*)((arrayCovers)->pCCovers))[(i) * 4 + (0)] : ((ulong*)((arrayCovers)->pCCovers))[(i) * 4 + (0)]);
                ioldPosEnd = ioldPos + ilen;
                if (ioldPosEnd > oldPos)
                {
                    if (ioldPos < oldPosEnd)
                    {
                        ulong from;
                        nuint copyLen;
                        ulong dstPos = (((arrayCovers)->is32) != 0 ? ((uint*)((arrayCovers)->pCCovers))[(i) * 4 + (3)] : ((ulong*)((arrayCovers)->pCCovers))[(i) * 4 + (3)]);

                        if (ioldPos >= oldPos)
                        {
                            from = ioldPos;
                        }
                        else
                        {
                            from = oldPos;
                            dstPos += (oldPos - ioldPos);
                        }

                        copyLen = (nuint)(((ioldPosEnd <= oldPosEnd) ? ioldPosEnd : oldPosEnd) - from);
                        Unsafe.CopyBlockUnaligned(old_cache + (nuint)(dstPos), cache_buf + (from - oldPos), copyLen);
                        sumCacheLen -= copyLen;
                        if (((i == cur_i) ? 1 : 0 & (oldPosEnd >= ioldPosEnd) ? 1 : 0) != 0)
                        {
                            ++cur_i;
                        }
                    }
                    else
                    {
                        if ((i == cur_i) && (ioldPos - oldPosEnd >= kMinSpaceLen))
                        {
                            oldPosEnd = (((ulong)(ioldPos)) & (~(ulong)((kAccessPageSize) - 1)));
                        }

                        break;
                    }
                }
                else
                {
                    if (i == cur_i)
                    {
                        ++cur_i;
                    }
                }
            }

            oldPos = oldPosEnd;
        }

        _arrayCovers_sort_by_new(arrayCovers);
        ((void)((!!(sumCacheLen == 0)) || (_wassert("sumCacheLen==0", "patch.c", unchecked((uint)(1705))) , 0) != 0));
        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _cache_old_StreamInput_read([NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* stream, [NativeTypeName("hpatch_StreamPos_t")] ulong readFromPos, [NativeTypeName("unsigned char *")] byte* out_data, [NativeTypeName("unsigned char *")] byte* out_data_end)
    {
        _cache_old_TStreamInput* self = (_cache_old_TStreamInput*)(stream->StreamImport);
        ulong dataLen = (nuint)(self->readFromPosEnd - self->readFromPos);
        nuint readLen;

        if (unchecked(dataLen) == 0)
        {
            ulong oldPos;
            nuint i = self->arrayCovers.cur_index++;

            if (i >= self->arrayCovers.coverCount)
            {
                return 0;
            }

            oldPos = (((&self->arrayCovers)->is32) != 0 ? ((uint*)((&self->arrayCovers)->pCCovers))[(i) * 4 + (0)] : ((ulong*)((&self->arrayCovers)->pCCovers))[(i) * 4 + (0)]);
            dataLen = (((&self->arrayCovers)->is32) != 0 ? ((uint*)((&self->arrayCovers)->pCCovers))[(i) * 4 + (2)] : ((ulong*)((&self->arrayCovers)->pCCovers))[(i) * 4 + (2)]);
            self->isInHitCache = unchecked(dataLen <= self->maxCachedLen) ? 1 : 0;
            self->readFromPos = oldPos;
            self->readFromPosEnd = oldPos + dataLen;
        }

        readLen = out_data_end - out_data;
        if (unchecked(readLen > dataLen) || (self->readFromPos != readFromPos))
        {
            return 0;
        }

        self->readFromPos = readFromPos + readLen;
        if ((self->isInHitCache) != 0)
        {
            (unchecked((void)((!!(readLen <= (nuint)(self->cachesEnd - self->caches))) || (_wassert("readLen<=(hpatch_size_t)(self->cachesEnd-self->caches)", "patch.c", (uint)(1740)) , 0) != 0)));
            Unsafe.CopyBlockUnaligned(out_data, self->caches, readLen);
            self->caches += readLen;
            return true;
        }
        else
        {
            return self->oldData->read(self->oldData, readFromPos, out_data, out_data_end);
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _cache_old(HPatchStreamInput** out_cachedOld, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, _TArrayCovers* arrayCovers, [NativeTypeName("hpatch_BOOL *")] int* out_isReadError, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte **")] byte** ptemp_cache_end, [NativeTypeName("TByte *")] byte* cache_buf_end)
    {
        _cache_old_TStreamInput* self;
        byte* temp_cache_end = *ptemp_cache_end;
        ulong oldPosBegin;
        ulong oldPosEnd;
        nuint sumCacheLen;
        nuint maxCachedLen;

        *out_isReadError = 0;

        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (sizeof(HPatchStreamInput))))
            {
                return 0;
            }

            (*out_cachedOld) = (HPatchStreamInput*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(*out_cachedOld) + (nuint)unchecked(sizeof(HPatchStreamInput));
        }

        ;

        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (sizeof(_cache_old_TStreamInput))))
            {
                return 0;
            }

            (self) = (_cache_old_TStreamInput*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
            temp_cache = (byte*)(self) + (nuint)unchecked(sizeof(_cache_old_TStreamInput));
        }

        ;
        maxCachedLen = _getMaxCachedLen(arrayCovers, temp_cache, temp_cache_end, cache_buf_end);
        if (maxCachedLen == 0)
        {
            return 0;
        }

        sumCacheLen = _set_cache_pos(arrayCovers, maxCachedLen, &oldPosBegin, &oldPosEnd);
        if (sumCacheLen == 0)
        {
            return 0;
        }

        temp_cache_end = temp_cache + sumCacheLen;
        if (_cache_old_load(oldData, oldPosBegin, oldPosEnd, arrayCovers, maxCachedLen, sumCacheLen, temp_cache, temp_cache_end, cache_buf_end) == 0)
        {
            *out_isReadError = true;
            return 0;
        }


        {
            self->arrayCovers = *arrayCovers;
            self->arrayCovers.cur_index = 0;
            self->isInHitCache = 0;
            self->maxCachedLen = maxCachedLen;
            self->caches = temp_cache;
            self->cachesEnd = temp_cache_end;
            self->readFromPos = 0;
            self->readFromPosEnd = 0;
            self->oldData = oldData;
            (*out_cachedOld)->StreamImport = self;
            (*out_cachedOld)->StreamSize = oldData->StreamSize;
            (*out_cachedOld)->read = _cache_old_StreamInput_read;
            *ptemp_cache_end = temp_cache_end;
        }

        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _patch_cache(IHPatchCovers** out_covers, [NativeTypeName("const hpatch_TStreamInput **")] HPatchStreamInput** poldData, [NativeTypeName("hpatch_StreamPos_t")] ulong newDataSize, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* diffData, [NativeTypeName("hpatch_BOOL")] int isCompressedDiff, HPatchDecompress* decompressPlugin, [NativeTypeName("size_t")] nuint kCacheCount, [NativeTypeName("TByte **")] byte** ptemp_cache, [NativeTypeName("TByte **")] byte** ptemp_cache_end, [NativeTypeName("hpatch_BOOL *")] int* out_isReadError)
    {
        HPatchStreamInput* oldData = *poldData;
        nuint kMinCacheSize = (1024 * 4) * kCacheCount;
        nuint kBestACacheSize = (1024 * 64);
        nuint _minActiveSize = (1 << 20) * 8;
        ulong _betterActiveSize = kBestACacheSize * kCacheCount * 2 + oldData->StreamSize / 8;
        nuint kActiveCacheOldMemorySize = (_betterActiveSize > _minActiveSize) ? _minActiveSize : (nuint)(_betterActiveSize);
        byte* temp_cache = *ptemp_cache;
        byte* temp_cache_end = *ptemp_cache_end;

        *out_isReadError = 0;
        if (unchecked((nuint)(temp_cache_end - temp_cache)) >= unchecked(oldData->StreamSize + kMinCacheSize + sizeof(HPatchStreamInput) + sizeof(ulong)))
        {
            HPatchStreamInput* replace_oldData = null;


            {
                if (unchecked((nuint)(temp_cache_end - temp_cache)) < unchecked(sizeof(ulong) + (sizeof(HPatchStreamInput))))
                {
                    return 0;
                }

                (replace_oldData) = (HPatchStreamInput*)(unchecked(((nuint)(((nuint)(temp_cache)) + ((sizeof(ulong)) - 1))) & (~(nuint)((sizeof(ulong)) - 1))));
                temp_cache = (byte*)(replace_oldData) + (nuint)unchecked(sizeof(HPatchStreamInput));
            }

            ;
            if (_cache_load_all(oldData, temp_cache_end - oldData->StreamSize, temp_cache_end) == 0)
            {
                *out_isReadError = true;
                return 0;
            }

            _ = mem_as_hStreamInput(replace_oldData, temp_cache_end - oldData->StreamSize, temp_cache_end);
            temp_cache_end -= oldData->StreamSize;
            *out_covers = null;
            *poldData = replace_oldData;
            *ptemp_cache = temp_cache;
            *ptemp_cache_end = temp_cache_end;
            return true;
        }
        else if (unchecked((nuint)(temp_cache_end - temp_cache)) >= unchecked(kActiveCacheOldMemorySize))
        {
            int isUsedCover32;
            byte* temp_cache_end_back = temp_cache_end;
            _TArrayCovers* arrayCovers = null;

            (unchecked((void)((!!((nuint)(temp_cache_end - temp_cache) > kBestACacheSize * kCacheCount)) || (_wassert("(hpatch_size_t)(temp_cache_end-temp_cache)>kBestACacheSize*kCacheCount", "patch.c", (uint)(1835)) , 0) != 0)));
            ((void)(unchecked((!!(kBestACacheSize > sizeof(_TCompressedCovers) + sizeof(PackedCovers))) || (_wassert("kBestACacheSize>sizeof(_TCompressedCovers)+sizeof(_TPackedCovers)", "patch.c", (uint)(1836)) , 0) != 0)));
            if ((isCompressedDiff) != 0)
            {
                HPatchCompressedDiffInfo diffInfo = new HPatchCompressedDiffInfo();
                _TCompressedCovers* compressedCovers = null;

                if (_compressedCovers_open(&compressedCovers, &diffInfo, diffData, decompressPlugin, temp_cache_end - kBestACacheSize - sizeof(_TCompressedCovers), temp_cache_end) == 0)
                {
                    *out_isReadError = true;
                    return 0;
                }

                if ((oldData->StreamSize != diffInfo.oldDataSize) || (newDataSize != diffInfo.newDataSize))
                {
                    *out_isReadError = true;
                    return 0;
                }

                temp_cache_end -= kBestACacheSize + sizeof(_TCompressedCovers);
                *out_covers = &compressedCovers->base.ICovers;
                isUsedCover32 = unchecked(((diffInfo.oldDataSize | diffInfo.newDataSize) < ((ulong)(1) << 32)) ? 1 : 0);
            }
            else
            {
                PackedCovers* packedCovers = null;
                HDiffHead diffHead = new HDiffHead();
                ulong oldDataSize = oldData->StreamSize;

                if (_packedCovers_open(&packedCovers, &diffHead, diffData, temp_cache_end - kBestACacheSize * 3 - sizeof(PackedCovers), temp_cache_end) == 0)
                {
                    *out_isReadError = true;
                    return 0;
                }

                temp_cache_end -= kBestACacheSize * 3 + sizeof(PackedCovers);
                *out_covers = &packedCovers->base.ICovers;
                isUsedCover32 = unchecked(((oldDataSize | newDataSize) < ((ulong)(1) << 32)) ? 1 : 0);
            }

            if (_arrayCovers_load(&arrayCovers, *out_covers, isUsedCover32, out_isReadError, &temp_cache, temp_cache_end - kBestACacheSize) == 0)
            {
                if ((*out_isReadError) != 0)
                {
                    return 0;
                }

                *ptemp_cache = temp_cache;
                *ptemp_cache_end = temp_cache_end;
                return 0;
            }
            else
            {
                byte* old_cache_end;
                HPatchStreamInput* replace_oldData = null;

                ((void)((!!(*out_isReadError == 0)) || (_wassert("!(*out_isReadError)", "patch.c", unchecked((uint)(1877))) , 0) != 0));
                if ((*out_covers)->close(*out_covers) == 0)
                {
                    return 0;
                }

                *out_covers = &arrayCovers->ICovers;
                temp_cache_end = temp_cache_end_back;
                old_cache_end = temp_cache_end - kBestACacheSize * kCacheCount;
                if (unchecked((nuint)(temp_cache_end - temp_cache) <= kBestACacheSize * kCacheCount) || (_cache_old(&replace_oldData, oldData, arrayCovers, out_isReadError, temp_cache, &old_cache_end, temp_cache_end) == 0))
                {
                    if ((*out_isReadError) != 0)
                    {
                        return 0;
                    }

                    *ptemp_cache = temp_cache;
                    *ptemp_cache_end = temp_cache_end;
                    return 0;
                }
                else
                {
                    ((void)((!!(*out_isReadError == 0)) || (_wassert("!(*out_isReadError)", "patch.c", unchecked((uint)(1895))) , 0) != 0));
                    (unchecked((void)((!!((nuint)(temp_cache_end - old_cache_end) >= kBestACacheSize * kCacheCount)) || (_wassert("(hpatch_size_t)(temp_cache_end-old_cache_end)>=kBestACacheSize*kCacheCount", "patch.c", (uint)(1896)) , 0) != 0)));
                    temp_cache = old_cache_end;
                    *poldData = replace_oldData;
                    *ptemp_cache = temp_cache;
                    *ptemp_cache_end = temp_cache_end;
                    return true;
                }
            }
        }

        return 0;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_stream_with_cache([NativeTypeName("const struct hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const struct hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const struct hpatch_TStreamInput *")] HPatchStreamInput* serializedDiff, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end)
    {
        int result;
        IHPatchCovers* covers = null;
        int isReadError = 0;

        _ = _patch_cache(&covers, &oldData, out_newData->StreamSize, serializedDiff, 0, null, 8, &temp_cache, &temp_cache_end, &isReadError);
        if ((isReadError) != 0)
        {
            return 0;
        }

        result = _patch_stream_with_cache(out_newData, oldData, serializedDiff, covers, temp_cache, temp_cache_end);
        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_stream([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* serializedDiff)
    {
        byte[] temp_cache;

        return _patch_stream_with_cache(out_newData, oldData, serializedDiff, null, temp_cache, temp_cache + sizeof(byte) / sizeof(byte));
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_decompress_with_cache([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedDiff, HPatchDecompress* decompressPlugin, [NativeTypeName("TByte *")] byte* temp_cache, [NativeTypeName("TByte *")] byte* temp_cache_end)
    {
        int result;
        IHPatchCovers* covers = null;
        int isReadError = 0;

        _ = _patch_cache(&covers, &oldData, out_newData->StreamSize, compressedDiff, true, decompressPlugin, 6, &temp_cache, &temp_cache_end, &isReadError);
        if ((isReadError) != 0)
        {
            return 0;
        }

        result = _patch_decompress_cache(out_newData, null, oldData, compressedDiff, decompressPlugin, covers, temp_cache, temp_cache_end);
        if ((covers != null) && (covers->close(covers) == 0))
        {
            result = 0;
        }

        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_decompress([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedDiff, HPatchDecompress* decompressPlugin)
    {
        byte[] temp_cache;

        return _patch_decompress_cache(out_newData, null, oldData, compressedDiff, decompressPlugin, null, temp_cache, temp_cache + sizeof(byte) / sizeof(byte));
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int hpatch_coverList_open_serializedDiff(hpatch_TCoverList* out_coverList, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* serializedDiff)
    {
        byte* temp_cache;
        byte* temp_cache_end;
        PackedCovers* packedCovers = null;
        HDiffHead diffHead = new HDiffHead();

        ((void)((!!((out_coverList != null) && (out_coverList->ICovers == null))) || (_wassert("(out_coverList!=0)&&(out_coverList->ICovers==0)", "patch.c", unchecked((uint)(1966))) , 0) != 0));
        temp_cache = out_coverList->_buf;
        temp_cache_end = temp_cache + sizeof(byte);
        if (_packedCovers_open(&packedCovers, &diffHead, serializedDiff, temp_cache, temp_cache_end) == 0)
        {
            return 0;
        }

        out_coverList->ICovers = &packedCovers->base.ICovers;
        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int hpatch_coverList_open_compressedDiff(hpatch_TCoverList* out_coverList, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedDiff, HPatchDecompress* decompressPlugin)
    {
        byte* temp_cache;
        byte* temp_cache_end;
        _TCompressedCovers* compressedCovers = null;
        HPatchCompressedDiffInfo diffInfo = new HPatchCompressedDiffInfo();

        ((void)((!!((out_coverList != null) && (out_coverList->ICovers == null))) || (_wassert("(out_coverList!=0)&&(out_coverList->ICovers==0)", "patch.c", unchecked((uint)(1983))) , 0) != 0));
        temp_cache = out_coverList->_buf;
        temp_cache_end = temp_cache + sizeof(byte);
        if (_compressedCovers_open(&compressedCovers, &diffInfo, compressedDiff, decompressPlugin, temp_cache, temp_cache_end) == 0)
        {
            return 0;
        }

        out_coverList->ICovers = &compressedCovers->base.ICovers;
        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_single_compressed_diff([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* singleCompressedDiff, [NativeTypeName("hpatch_StreamPos_t")] ulong diffData_pos, [NativeTypeName("hpatch_StreamPos_t")] ulong uncompressedSize, [NativeTypeName("hpatch_StreamPos_t")] ulong compressedSize, HPatchDecompress* decompressPlugin, [NativeTypeName("hpatch_StreamPos_t")] ulong coverCount, [NativeTypeName("hpatch_size_t")] nuint stepMemSize, [NativeTypeName("unsigned char *")] byte* temp_cache, [NativeTypeName("unsigned char *")] byte* temp_cache_end, sspatch_coversListener_t* coversListener)
    {
        int result;
        hpatch_TUncompresser_t uncompressedStream = new hpatch_TUncompresser_t();
        ulong diffData_posEnd;

        uncompressedStream = default;
        if (compressedSize == 0)
        {
            decompressPlugin = null;
        }
        else
        {
            if (decompressPlugin == null)
            {
                return 0;
            }
        }

        diffData_posEnd = ((decompressPlugin) != null ? compressedSize : uncompressedSize) + diffData_pos;
        if (diffData_posEnd > singleCompressedDiff->StreamSize)
        {
            return 0;
        }

        if ((decompressPlugin) != null)
        {
            if (compressed_stream_as_uncompressed(&uncompressedStream, uncompressedSize, decompressPlugin, singleCompressedDiff, diffData_pos, diffData_posEnd) == 0)
            {
                return 0;
            }

            singleCompressedDiff = &uncompressedStream.base;
            diffData_pos = 0;
            diffData_posEnd = singleCompressedDiff->StreamSize;
        }

        result = patch_single_stream_diff(out_newData, oldData, singleCompressedDiff, diffData_pos, diffData_posEnd, coverCount, stepMemSize, temp_cache, temp_cache_end, coversListener);
        if ((decompressPlugin) != null)
        {
            close_compressed_stream_as_uncompressed(&uncompressedStream);
        }

        return result;
    }

    [NativeTypeName("const size_t")]
    public const nuint _kStepMemSizeSafeLimit = (1 << 20) * 16;

    [return: NativeTypeName("hpatch_BOOL")]
    public static int getSingleCompressedDiffInfo(hpatch_singleCompressedDiffInfo* out_diffInfo, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* singleCompressedDiff, [NativeTypeName("hpatch_StreamPos_t")] ulong diffInfo_pos)
    {
        StreamCacheClip _diffHeadClip = new StreamCacheClip();
        StreamCacheClip* diffHeadClip = &_diffHeadClip;
        byte[] temp_cache;

        _TStreamCacheClip_init(&_diffHeadClip, singleCompressedDiff, diffInfo_pos, singleCompressedDiff->StreamSize, temp_cache, (1024 * 4));

        {
            sbyte* kVersionType = new byte[] { 0x48, 0x44, 0x49, 0x46, 0x46, 0x53, 0x46, 0x32, 0x30, 0x00 };
            sbyte* tempType = out_diffInfo->compressType;

            if (_TStreamCacheClip_readType_end(diffHeadClip, '&', tempType) == 0)
            {
                return 0;
            }

            if (0 != strcmp(tempType, kVersionType))
            {
                return 0;
            }
        }


        {
            if (_TStreamCacheClip_readType_end(diffHeadClip, '\0', out_diffInfo->compressType) == 0)
            {
                return 0;
            }
        }


        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->newDataSize, 0) == 0)
            {
                return 0;
            }
        }

        ;

        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->oldDataSize, 0) == 0)
            {
                return 0;
            }
        }

        ;

        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->coverCount, 0) == 0)
            {
                return 0;
            }
        }

        ;

        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->stepMemSize, 0) == 0)
            {
                return 0;
            }
        }

        ;

        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->uncompressedSize, 0) == 0)
            {
                return 0;
            }
        }

        ;

        {
            if (_TStreamCacheClip_unpackUIntWithTag(diffHeadClip, &out_diffInfo->compressedSize, 0) == 0)
            {
                return 0;
            }
        }

        ;
        out_diffInfo->diffDataPos = ((diffHeadClip)->StreamPos - ((nuint)((diffHeadClip)->CacheEnd - (diffHeadClip)->CacheBegin))) - diffInfo_pos;
        if (out_diffInfo->compressedSize > out_diffInfo->uncompressedSize)
        {
            return 0;
        }

        if (out_diffInfo->stepMemSize > (out_diffInfo->newDataSize >= _kStepMemSizeSafeLimit ? out_diffInfo->newDataSize : _kStepMemSizeSafeLimit))
        {
            return 0;
        }

        if (out_diffInfo->stepMemSize > out_diffInfo->uncompressedSize)
        {
            return 0;
        }

        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _TUncompresser_read([NativeTypeName("const struct hpatch_TStreamInput *")] HPatchStreamInput* stream, [NativeTypeName("hpatch_StreamPos_t")] ulong readFromPos, [NativeTypeName("unsigned char *")] byte* out_data, [NativeTypeName("unsigned char *")] byte* out_data_end)
    {
        hpatch_TUncompresser_t* self = (hpatch_TUncompresser_t*)(stream->StreamImport);

        return self->_decompressPlugin->decompress_part(self->_decompressHandle, out_data, out_data_end);
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int compressed_stream_as_uncompressed(hpatch_TUncompresser_t* uncompressedStream, [NativeTypeName("hpatch_StreamPos_t")] ulong uncompressedSize, HPatchDecompress* decompressPlugin, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* compressedStream, [NativeTypeName("hpatch_StreamPos_t")] ulong compressed_pos, [NativeTypeName("hpatch_StreamPos_t")] ulong compressed_end)
    {
        hpatch_TUncompresser_t* self = uncompressedStream;

        ((void)((!!(decompressPlugin != null)) || (_wassert("decompressPlugin!=0", "patch.c", unchecked((uint)(2077))) , 0) != 0));
        ((void)((!!(self->_decompressHandle == null)) || (_wassert("self->_decompressHandle==0", "patch.c", unchecked((uint)(2078))) , 0) != 0));
        self->_decompressHandle = decompressPlugin->open(decompressPlugin, uncompressedSize, compressedStream, compressed_pos, compressed_end);
        if (self->_decompressHandle == null)
        {
            return 0;
        }

        self->_decompressPlugin = decompressPlugin;
        self->base.streamImport = self;
        self->base.streamSize = uncompressedSize;
        self->base.read = _TUncompresser_read;
        return true;
    }

    public static void close_compressed_stream_as_uncompressed(hpatch_TUncompresser_t* uncompressedStream)
    {
        hpatch_TUncompresser_t* self = uncompressedStream;

        if (self == null)
        {
            return;
        }

        if (self->_decompressHandle == null)
        {
            return;
        }

        _ = self->_decompressPlugin->close(self->_decompressPlugin, self->_decompressHandle);
        self->_decompressHandle = null;
    }

    public static void _rle0_decoder_init(rle0_decoder_t* self, [NativeTypeName("const unsigned char *")] byte* code, [NativeTypeName("const unsigned char *")] byte* code_end)
    {
        self->code = code;
        self->code_end = code_end;
        self->len0 = 0;
        self->lenv = 0;
        self->isNeedDecode0 = true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _rle0_decoder_add(rle0_decoder_t* self, [NativeTypeName("TByte *")] byte* out_data, [NativeTypeName("hpatch_size_t")] nuint decodeSize)
    {
        if ((self->len0) != 0)
        {
            _0_process:
            if (self->len0 >= decodeSize)
            {
                self->len0 -= decodeSize;
                return true;
            }
            else
            {
                decodeSize -= self->len0;
                out_data += self->len0;
                self->len0 = 0;
                goto _decode_v_process;
            }
        }

        if ((self->lenv) != 0)
        {
            _v_process:
            if (self->lenv >= decodeSize)
            {
                addData(out_data, self->code, decodeSize);
                self->code += decodeSize;
                self->lenv -= decodeSize;
                return true;
            }
            else
            {
                addData(out_data, self->code, self->lenv);
                out_data += self->lenv;
                decodeSize -= self->lenv;
                self->code += self->lenv;
                self->lenv = 0;
                goto _decode_0_process;
            }
        }

        ((void)((!!(decodeSize > 0)) || (_wassert("decodeSize>0", "patch.c", unchecked((uint)(2145))) , 0) != 0));
        if ((self->isNeedDecode0) != 0)
        {
            ulong len0;

            _decode_0_process:
            self->isNeedDecode0 = 0;
            if (hpatch_unpackUIntWithTag(&self->code, self->code_end, &len0, 0) == 0)
            {
                return 0;
            }

            if (len0 != unchecked((nuint)(len0)))
            {
                return 0;
            }

            self->len0 = (nuint)(len0);
            goto _0_process;
        }
        else
        {
            ulong lenv;

            _decode_v_process:
            self->isNeedDecode0 = true;
            if (hpatch_unpackUIntWithTag(&self->code, self->code_end, &lenv, 0) == 0)
            {
                return 0;
            }

            if (lenv > unchecked((nuint)(self->code_end - self->code)))
            {
                return 0;
            }

            self->lenv = (nuint)(lenv);
            goto _v_process;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _patch_add_old_with_rle0(OutStreamCache* outCache, rle0_decoder_t* rle0_decoder, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* old, [NativeTypeName("hpatch_StreamPos_t")] ulong oldPos, [NativeTypeName("hpatch_StreamPos_t")] ulong addLength, [NativeTypeName("TByte *")] byte* aCache, [NativeTypeName("hpatch_size_t")] nuint aCacheSize)
    {
        while (addLength > 0)
        {
            nuint decodeStep = aCacheSize;

            if (decodeStep > addLength)
            {
                decodeStep = (nuint)(addLength);
            }

            if (old->read(old, oldPos, aCache, aCache + decodeStep) == 0)
            {
                return 0;
            }

            if (_rle0_decoder_add(rle0_decoder, aCache, decodeStep) == 0)
            {
                return 0;
            }

            if (_TOutStreamCache_write(outCache, aCache, decodeStep) == 0)
            {
                return 0;
            }

            oldPos += decodeStep;
            addLength -= decodeStep;
        }

        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int sspatch_covers_nextCover(sspatch_covers_t* self)
    {
        int inc_oldPos_sign = (*(self->covers_cache)) >> (8 - 1);

        self->lastOldEnd = self->cover.OldPos + self->cover.Length;
        self->lastNewEnd = self->cover.NewPos + self->cover.Length;
        if (hpatch_unpackUIntWithTag(&self->covers_cache, self->covers_cacheEnd, &self->cover.OldPos, 1) == 0)
        {
            return 0;
        }

        if (inc_oldPos_sign == 0)
        {
            self->cover.OldPos += self->lastOldEnd;
        }
        else
        {
            self->cover.OldPos = self->lastOldEnd - self->cover.OldPos;
        }

        if (hpatch_unpackUIntWithTag(&self->covers_cache, self->covers_cacheEnd, &self->cover.NewPos, 0) == 0)
        {
            return 0;
        }

        self->cover.NewPos += self->lastNewEnd;
        if (hpatch_unpackUIntWithTag(&self->covers_cache, self->covers_cacheEnd, &self->cover.Length, 0) == 0)
        {
            return 0;
        }

        return true;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_single_stream_diff([NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* uncompressedDiffData, [NativeTypeName("hpatch_StreamPos_t")] ulong diffData_pos, [NativeTypeName("hpatch_StreamPos_t")] ulong diffData_posEnd, [NativeTypeName("hpatch_StreamPos_t")] ulong coverCount, [NativeTypeName("hpatch_size_t")] nuint stepMemSize, [NativeTypeName("unsigned char *")] byte* temp_cache, [NativeTypeName("unsigned char *")] byte* temp_cache_end, sspatch_coversListener_t* coversListener)
    {
        byte* step_cache = temp_cache;
        nuint cache_size;
        StreamCacheClip inClip = new StreamCacheClip();
        OutStreamCache outCache = new OutStreamCache();
        sspatch_covers_t covers = new sspatch_covers_t();

        ((void)((!!(diffData_posEnd <= uncompressedDiffData->StreamSize)) || (_wassert("diffData_posEnd<=uncompressedDiffData->streamSize", "patch.c", unchecked((uint)(2211))) , 0) != 0));
        sspatch_covers_init(&covers);
        if ((coversListener) != null)
        {
            ((void)((!(coversListener->onStepCovers == null)) || (_wassert("coversListener->onStepCovers", "patch.c", unchecked((uint)(2213))) , 0) != 0));
        }


        {
            if (unchecked((nuint)(temp_cache_end - temp_cache)) < stepMemSize + (1024 * 4) * 3)
            {
                return 0;
            }

            temp_cache += stepMemSize;
            cache_size = (temp_cache_end - temp_cache) / 3;
            _TStreamCacheClip_init(&inClip, uncompressedDiffData, diffData_pos, diffData_posEnd, temp_cache, cache_size);
            temp_cache += cache_size;
            _TOutStreamCache_init(&outCache, out_newData, temp_cache + cache_size, cache_size);
        }

        while ((coverCount) != 0)
        {
            rle0_decoder_t rle0_decoder = new rle0_decoder_t();


            {
                byte* covers_cacheEnd;
                byte* bufRle_cache_end;


                {
                    ulong bufCover_size;
                    ulong bufRle_size;


                    {
                        if (_TStreamCacheClip_unpackUIntWithTag(&inClip, &bufCover_size, 0) == 0)
                        {
                            return 0;
                        }
                    }

                    ;

                    {
                        if (_TStreamCacheClip_unpackUIntWithTag(&inClip, &bufRle_size, 0) == 0)
                        {
                            return 0;
                        }
                    }

                    ;
                    if (((bufCover_size > stepMemSize) ? 1 : 0 | (bufRle_size > stepMemSize) ? 1 : 0 | (bufCover_size + bufRle_size > stepMemSize) ? 1 : 0) != 0)
                    {
                        return 0;
                    }

                    covers_cacheEnd = step_cache + (nuint)(bufCover_size);
                    bufRle_cache_end = covers_cacheEnd + (nuint)(bufRle_size);
                }

                if ((coversListener) != null && (coversListener->onStepCoversReset) != null)
                {
                    coversListener->onStepCoversReset(coversListener, coverCount);
                }

                if (_TStreamCacheClip_readDataTo(&inClip, step_cache, bufRle_cache_end) == 0)
                {
                    return 0;
                }

                if ((coversListener) != null)
                {
                    coversListener->onStepCovers(coversListener, step_cache, covers_cacheEnd);
                }

                sspatch_covers_setCoversCache(&covers, step_cache, covers_cacheEnd);
                _rle0_decoder_init(&rle0_decoder, covers_cacheEnd, bufRle_cache_end);
            }

            while ((sspatch_covers_isHaveNextCover(&covers)) != 0)
            {
                if (sspatch_covers_nextCover(&covers) == 0)
                {
                    return 0;
                }

                if (covers.cover.NewPos > covers.lastNewEnd)
                {
                    if (_TOutStreamCache_copyFromClip(&outCache, &inClip, covers.cover.NewPos - covers.lastNewEnd) == 0)
                    {
                        return 0;
                    }
                }

                --coverCount;
                if ((covers.cover.Length) != 0)
                {
                    if ((unchecked((covers.cover.OldPos > oldData->StreamSize) ? 1 : 0 | (covers.cover.Length > (ulong)(oldData->StreamSize - covers.cover.OldPos)) ? 1 : 0)) != 0)
                    {
                        return 0;
                    }

                    if (_patch_add_old_with_rle0(&outCache, &rle0_decoder, oldData, covers.cover.OldPos, covers.cover.Length, temp_cache, cache_size) == 0)
                    {
                        return 0;
                    }
                }
                else
                {
                    if (coverCount != 0)
                    {
                        return 0;
                    }
                }
            }
        }

        if (_TOutStreamCache_flush(&outCache) == 0)
        {
            return 0;
        }

        if ((unchecked((0 == ((ulong)((&inClip)->StreamPosEnd - (&inClip)->StreamPos) + (ulong)((nuint)((&inClip)->CacheEnd - (&inClip)->CacheBegin)))) ? 1 : 0 & _TOutStreamCache_isFinish(&outCache) & (coverCount == 0) ? 1 : 0)) != 0)
        {
            return true;
        }
        else
        {
            return 0;
        }
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int _TDiffToSingleStream_read([NativeTypeName("const struct hpatch_TStreamInput *")] HPatchStreamInput* stream, [NativeTypeName("hpatch_StreamPos_t")] ulong readFromPos, [NativeTypeName("unsigned char *")] byte* out_data, [NativeTypeName("unsigned char *")] byte* out_data_end)
    {
        TDiffToSingleStream* self = (TDiffToSingleStream*)(stream->StreamImport);
        ulong readedSize = self->readedSize;

        while ((1) != 0)
        {
            nuint rLen = out_data_end - out_data;

            if (readFromPos == readedSize)
            {
                int result = self->diffStream->read(self->diffStream, readedSize, out_data, out_data_end);

                self->readedSize = readedSize + rLen;
                if ((self->isInSingleStream) != 0 || (rLen > (1024 * 4)))
                {
                    self->cachedBufBegin = (1024 * 4);
                }
                else
                {
                    if (rLen >= (1024 * 4))
                    {
                        Unsafe.CopyBlockUnaligned(self->buf, out_data_end - (1024 * 4), (1024 * 4));
                        self->cachedBufBegin = 0;
                    }
                    else
                    {
                        nuint new_cachedBufBegin;

                        if (self->cachedBufBegin >= rLen)
                        {
                            new_cachedBufBegin = self->cachedBufBegin - rLen;
                            _ = memmove(self->buf + new_cachedBufBegin, self->buf + self->cachedBufBegin, (1024 * 4) - self->cachedBufBegin);
                        }
                        else
                        {
                            new_cachedBufBegin = 0;
                            _ = memmove(self->buf, self->buf + rLen, (1024 * 4) - rLen);
                        }

                        Unsafe.CopyBlockUnaligned(self->buf + ((1024 * 4) - rLen), out_data, rLen);
                        self->cachedBufBegin = new_cachedBufBegin;
                    }
                }

                return result;
            }
            else
            {
                nuint cachedSize = (1024 * 4) - self->cachedBufBegin;
                nuint bufSize = (nuint)(readedSize - readFromPos);

                if ((unchecked((readFromPos < readedSize) ? 1 : 0 & (bufSize <= cachedSize) ? 1 : 0)) != 0)
                {
                    if (rLen > unchecked(bufSize))
                    {
                        rLen = bufSize;
                    }

                    Unsafe.CopyBlockUnaligned(out_data, self->buf + ((1024 * 4) - bufSize), rLen);
                    out_data += rLen;
                    readFromPos += rLen;
                    if (out_data == out_data_end)
                    {
                        return true;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    return 0;
                }
            }
        }
    }

    public static void TDiffToSingleStream_init(TDiffToSingleStream* self, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* diffStream)
    {
        self->base.streamImport = self;
        self->base.streamSize = diffStream->StreamSize;
        self->base.read = _TDiffToSingleStream_read;
        self->base._private_reserved = null;
        self->diffStream = diffStream;
        self->readedSize = 0;
        self->cachedBufBegin = (1024 * 4);
        self->isInSingleStream = 0;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_single_stream(sspatch_listener_t* listener, [NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* __out_newData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* oldData, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* singleCompressedDiff, [NativeTypeName("hpatch_StreamPos_t")] ulong diffInfo_pos, sspatch_coversListener_t* coversListener)
    {
        int result = true;
        HPatchDecompress* decompressPlugin = null;
        byte* temp_cache = null;
        byte* temp_cacheEnd = null;
        hpatch_singleCompressedDiffInfo diffInfo = new hpatch_singleCompressedDiffInfo();
        HPatchStreamOutput _out_newData = *__out_newData;
        HPatchStreamOutput* out_newData = &_out_newData;
        TDiffToSingleStream _toSStream = new TDiffToSingleStream();

        ((void)((!!((listener) != null && (listener->onDiffInfo) != null)) || (_wassert("(listener)&&(listener->onDiffInfo)", "patch.c", unchecked((uint)(2359))) , 0) != 0));
        TDiffToSingleStream_init(&_toSStream, singleCompressedDiff);
        singleCompressedDiff = &_toSStream.base;
        if (getSingleCompressedDiffInfo(&diffInfo, singleCompressedDiff, diffInfo_pos) == 0)
        {
            return 0;
        }

        if (diffInfo.newDataSize > out_newData->StreamSize)
        {
            return 0;
        }

        out_newData->StreamSize = diffInfo.newDataSize;
        if (diffInfo.oldDataSize != oldData->StreamSize)
        {
            return 0;
        }

        if (listener->onDiffInfo(listener, &diffInfo, &decompressPlugin, &temp_cache, &temp_cacheEnd) == 0)
        {
            return 0;
        }

        if ((temp_cache == null) || (temp_cache >= temp_cacheEnd))
        {
            result = 0;
        }

        if ((result) != 0)
        {
            result = patch_single_compressed_diff(out_newData, oldData, singleCompressedDiff, diffInfo.diffDataPos, diffInfo.uncompressedSize, diffInfo.compressedSize, decompressPlugin, diffInfo.coverCount, unchecked((nuint)(diffInfo.stepMemSize)), temp_cache, temp_cacheEnd, coversListener);
        }

        if ((listener->onPatchFinish) != null)
        {
            listener->onPatchFinish(listener, temp_cache, temp_cacheEnd);
        }

        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int getCompressedDiffInfo_mem(HPatchCompressedDiffInfo* out_diffInfo, [NativeTypeName("const unsigned char *")] byte* compressedDiff, [NativeTypeName("const unsigned char *")] byte* compressedDiff_end)
    {
        HPatchStreamInput diffStream = new HPatchStreamInput();

        _ = mem_as_hStreamInput(&diffStream, compressedDiff, compressedDiff_end);
        return getCompressedDiffInfo(out_diffInfo, &diffStream);
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_decompress_mem([NativeTypeName("unsigned char *")] byte* out_newData, [NativeTypeName("unsigned char *")] byte* out_newData_end, [NativeTypeName("const unsigned char *")] byte* oldData, [NativeTypeName("const unsigned char *")] byte* oldData_end, [NativeTypeName("const unsigned char *")] byte* compressedDiff, [NativeTypeName("const unsigned char *")] byte* compressedDiff_end, HPatchDecompress* decompressPlugin)
    {
        HPatchStreamOutput out_newStream = new HPatchStreamOutput();
        HPatchStreamInput oldStream = new HPatchStreamInput();
        HPatchStreamInput diffStream = new HPatchStreamInput();

        _ = mem_as_hStreamOutput(&out_newStream, out_newData, out_newData_end);
        _ = mem_as_hStreamInput(&oldStream, oldData, oldData_end);
        _ = mem_as_hStreamInput(&diffStream, compressedDiff, compressedDiff_end);
        return patch_decompress(&out_newStream, &oldStream, &diffStream, decompressPlugin);
    }

    public static void hpatch_coverList_init(hpatch_TCoverList* coverList)
    {
        ((void)((!!(coverList != null)) || (_wassert("coverList!=0", "patch.h", unchecked((uint)(131))) , 0) != 0));
        Unsafe.InitBlockUnaligned(coverList, 0, sizeof(hpatch_TCoverList) - sizeof(byte));
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int hpatch_coverList_close(hpatch_TCoverList* coverList)
    {
        int result = true;

        if ((coverList != null) && (coverList->ICovers) != null)
        {
            result = coverList->ICovers->close(coverList->ICovers);
            hpatch_coverList_init(coverList);
        }

        return result;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int patch_single_stream_mem(sspatch_listener_t* listener, [NativeTypeName("unsigned char *")] byte* out_newData, [NativeTypeName("unsigned char *")] byte* out_newData_end, [NativeTypeName("const unsigned char *")] byte* oldData, [NativeTypeName("const unsigned char *")] byte* oldData_end, [NativeTypeName("const unsigned char *")] byte* diff, [NativeTypeName("const unsigned char *")] byte* diff_end, sspatch_coversListener_t* coversListener)
    {
        HPatchStreamOutput out_newStream = new HPatchStreamOutput();
        HPatchStreamInput oldStream = new HPatchStreamInput();
        HPatchStreamInput diffStream = new HPatchStreamInput();

        _ = mem_as_hStreamOutput(&out_newStream, out_newData, out_newData_end);
        _ = mem_as_hStreamInput(&oldStream, oldData, oldData_end);
        _ = mem_as_hStreamInput(&diffStream, diff, diff_end);
        return patch_single_stream(listener, &out_newStream, &oldStream, &diffStream, 0, coversListener);
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int getSingleCompressedDiffInfo_mem(hpatch_singleCompressedDiffInfo* out_diffInfo, [NativeTypeName("const unsigned char *")] byte* singleCompressedDiff, [NativeTypeName("const unsigned char *")] byte* singleCompressedDiff_end)
    {
        HPatchStreamInput diffStream = new HPatchStreamInput();

        _ = mem_as_hStreamInput(&diffStream, singleCompressedDiff, singleCompressedDiff_end);
        return getSingleCompressedDiffInfo(out_diffInfo, &diffStream, 0);
    }

    [NativeTypeName("const hpatch_uint")]
    public const uint kByteRleType_bit = 2;





    public static void _TOutStreamCache_init(OutStreamCache* self, [NativeTypeName("const hpatch_TStreamOutput *")] HPatchStreamOutput* dstStream, [NativeTypeName("unsigned char *")] byte* aCache, [NativeTypeName("hpatch_size_t")] nuint aCacheSize)
    {
        self->writeToPos = 0;
        self->cacheCur = 0;
        self->dstStream = dstStream;
        self->cacheBuf = aCache;
        self->cacheEnd = aCacheSize;
    }

    public static void _TOutStreamCache_resetCache(OutStreamCache* self, [NativeTypeName("unsigned char *")] byte* aCache, [NativeTypeName("hpatch_size_t")] nuint aCacheSize)
    {
        ((void)((!!(0 == self->cacheCur)) || (_wassert("0==self->cacheCur", "patch_private.h", unchecked((uint)(154))) , 0) != 0));
        self->cacheBuf = aCache;
        self->cacheEnd = aCacheSize;
    }





    [return: NativeTypeName("hpatch_size_t")]
    public static nuint _TOutStreamCache_cachedDataSize([NativeTypeName("const _TOutStreamCache *")] OutStreamCache* self)
    {
        return self->cacheCur;
    }

    public static void TDiffToSingleStream_setInSingleStream(TDiffToSingleStream* self, [NativeTypeName("hpatch_StreamPos_t")] ulong singleStreamPos)
    {
        self->isInSingleStream = true;
    }

    public static void TDiffToSingleStream_resetStream(TDiffToSingleStream* self, [NativeTypeName("const hpatch_TStreamInput *")] HPatchStreamInput* diffStream)
    {
        self->diffStream = diffStream;
    }

    public static void _singleDiffInfoToHDiffInfo(HPatchCompressedDiffInfo* out_diffInfo, [NativeTypeName("const hpatch_singleCompressedDiffInfo *")] hpatch_singleCompressedDiffInfo* singleDiffInfo)
    {
        out_diffInfo->newDataSize = singleDiffInfo->newDataSize;
        out_diffInfo->oldDataSize = singleDiffInfo->oldDataSize;
        out_diffInfo->compressedCount = (singleDiffInfo->compressedSize > 0) ? 1 : 0;
        Unsafe.CopyBlockUnaligned(out_diffInfo->compressType, singleDiffInfo->compressType, strlen(singleDiffInfo->compressType) + 1);
    }

    public static void sspatch_covers_init(sspatch_covers_t* self)
    {
        Unsafe.InitBlockUnaligned(self, 0, (uint)(sizeof(sspatch_covers_t)));
    }

    public static void sspatch_covers_setCoversCache(sspatch_covers_t* self, [NativeTypeName("const unsigned char *")] byte* covers_cache, [NativeTypeName("const unsigned char *")] byte* covers_cacheEnd)
    {
        self->covers_cache = covers_cache;
        self->covers_cacheEnd = covers_cacheEnd;
    }

    [return: NativeTypeName("hpatch_BOOL")]
    public static int sspatch_covers_isHaveNextCover([NativeTypeName("const sspatch_covers_t *")] sspatch_covers_t* self)
    {
        return (self->covers_cache != (self)->covers_cacheEnd) ? 1 : 0;
    }
}