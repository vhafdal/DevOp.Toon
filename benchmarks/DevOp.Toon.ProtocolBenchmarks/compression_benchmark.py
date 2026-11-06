#!/usr/bin/env python3
"""Compare installed compression libraries and verify zstd CLI on identical UTF-8 payloads."""
import argparse, ctypes as C, ctypes.util, gzip, hashlib, json, pathlib, statistics, subprocess, time, zlib
p=argparse.ArgumentParser();p.add_argument('directory');p.add_argument('--output',default='/tmp/toon-compression-comparison.json');p.add_argument('--repeats',type=int,default=5);a=p.parse_args()
size=C.c_size_t; ptr=C.c_void_p
z=C.CDLL(ctypes.util.find_library('zstd'));be=C.CDLL(ctypes.util.find_library('brotlienc'));bd=C.CDLL(ctypes.util.find_library('brotlidec'))
def bind(lib,name,restype,args):
 f=getattr(lib,name);f.restype=restype;f.argtypes=args;return f
bound=bind(z,'ZSTD_compressBound',size,[size]);zc=bind(z,'ZSTD_compress',size,[ptr,size,ptr,size,C.c_int]);zd=bind(z,'ZSTD_decompress',size,[ptr,size,ptr,size]);ze=bind(z,'ZSTD_isError',C.c_uint,[size]);zn=bind(z,'ZSTD_getErrorName',C.c_char_p,[size]);zv=bind(z,'ZSTD_versionString',C.c_char_p,[])
boundb=bind(be,'BrotliEncoderMaxCompressedSize',size,[size]);bc=bind(be,'BrotliEncoderCompress',C.c_int,[C.c_int,C.c_int,C.c_int,size,ptr,C.POINTER(size),ptr]);bdec=bind(bd,'BrotliDecoderDecompress',C.c_int,[size,ptr,C.POINTER(size),ptr]);bv=bind(be,'BrotliEncoderVersion',C.c_uint,[])
def zcheck(n):
 if ze(n):raise RuntimeError(zn(n).decode())
 return n
def zcompress(data,level):
 cap=zcheck(bound(len(data)));out=C.create_string_buffer(cap)
 n=zcheck(zc(out,cap,C.c_char_p(data),len(data),level));return C.string_at(out,n)
def zdecompress(data,n):
 out=C.create_string_buffer(n);actual=zcheck(zd(out,n,C.c_char_p(data),len(data)))
 if actual!=n:raise RuntimeError('Zstd length mismatch')
 return C.string_at(out,actual)
def bcompress(data):
 cap=boundb(len(data));out=C.create_string_buffer(cap);n=size(cap)
 if not bc(4,22,0,len(data),C.c_char_p(data),C.byref(n),out):raise RuntimeError('Brotli compression failed')
 return C.string_at(out,n.value)
def bdecompress(data,n):
 out=C.create_string_buffer(n);written=size(n)
 if bdec(len(data),C.c_char_p(data),C.byref(written),out)!=1 or written.value!=n:raise RuntimeError('Brotli decode failed')
 return C.string_at(out,written.value)
def measure(f):
 samples=[];value=None
 for _ in range(a.repeats):
  t=time.perf_counter_ns();value=f();samples.append((time.perf_counter_ns()-t)/1e6)
 return value,statistics.median(samples),samples
result={'ZstdNativeVersion':zv().decode(),'ZstdCliVersion':subprocess.check_output(['zstd','--version'],text=True).strip(),'BrotliNativeVersionInteger':bv(),'ZlibVersion':zlib.ZLIB_RUNTIME_VERSION,'Repeats':a.repeats,'Notes':'Same exact UTF-8 bytes per row; source hashes and every decompressed result verified. Single-threaded one-shot native calls, fresh codec state, Python/ctypes buffer/result allocation included; no subprocess startup in native timing. Gzip uses Python stdlib/zlib, Brotli uses installed native library quality4/window22, zstd installed native levels1/3/6. Python and .NET API overhead differ: compare compression algorithms, not application/network latency. CLI zstd is separately verified once per level; CLI frame size can differ by checksum. No production dependency installed or changed. Peak memory is not measured here.','Results':[]}
files=sorted(pathlib.Path(a.directory).glob('*.toon'))+sorted(pathlib.Path(a.directory).glob('*.json'))
if not files:raise SystemExit('No exported payloads found')
for path in files:
 raw=path.read_bytes();raw.decode('utf-8');digest=hashlib.sha256(raw).hexdigest()
 codecs=[('gzip-6',lambda:gzip.compress(raw,compresslevel=6,mtime=0),gzip.decompress,None),('brotli-4',lambda:bcompress(raw),lambda x:bdecompress(x,len(raw)),None)]
 for level in [1,3,6]:codecs.append((f'zstd-{level}',lambda level=level:zcompress(raw,level),lambda x:zdecompress(x,len(raw)),level))
 for name,compress,decompress,level in codecs:
  packed=compress()
  if decompress(packed)!=raw:raise RuntimeError(f'{path.name} {name} failed byte equality')
  _,cm,cs=measure(compress);decoded,dm,ds=measure(lambda:decompress(packed))
  if decoded!=raw:raise RuntimeError('Timed decode differs')
  item={'Payload':path.name,'SourceSha256':digest,'SourceBytes':len(raw),'Codec':name,'CompressedBytes':len(packed),'CompressMilliseconds':cm,'DecompressMilliseconds':dm,'CompressSamples':cs,'DecompressSamples':ds,'ByteExact':True}
  if level is not None:
   cli=subprocess.run(['zstd','-q','-T1',f'-{level}','-c',str(path)],check=True,stdout=subprocess.PIPE).stdout
   cli_raw=subprocess.run(['zstd','-q','-d','-c'],input=cli,check=True,stdout=subprocess.PIPE).stdout
   if cli_raw!=raw:raise RuntimeError('CLI byte equality failed')
   item['CliCompressedBytes']=len(cli);item['CliByteExact']=True
  result['Results'].append(item)
pathlib.Path(a.output).write_text(json.dumps(result,indent=2)+'\n')
print('Payload | Codec | Source bytes | Compressed bytes | Compress ms | Decompress ms | Exact')
for r in result['Results']:print(f"{r['Payload']} | {r['Codec']} | {r['SourceBytes']} | {r['CompressedBytes']} | {r['CompressMilliseconds']:.3f} | {r['DecompressMilliseconds']:.3f} | {r['ByteExact']}")
