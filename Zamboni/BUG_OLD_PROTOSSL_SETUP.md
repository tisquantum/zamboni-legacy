# Bug_OldProtoSSL Setup for NHL Legacy

This guide explains how to create a "fake" SSL certificate that exploits a bug in older EA ProtoSSL clients (like NHL Legacy) to accept self-signed certificates without requiring client-side patches.

**Reference:** [Bug_OldProtoSSL GitHub Repository](https://github.com/Aim4kill/Bug_OldProtoSSL)

## Overview

The Bug_OldProtoSSL method exploits a vulnerability in older ProtoSSL implementations where changing the certificate's `algorithmIdentifier` from MD5/SHA1 to `RSA_PKCS_KEY` causes the hash size to be set to 0, effectively bypassing certificate signature verification.

This allows NHL Legacy to accept your self-signed certificate **without** requiring RPCS3 patches or client-side modifications.

## Prerequisites

1. **OpenSSL** - Install if not already available:
   ```bash
   # Ubuntu/Debian
   sudo apt-get install openssl
   
   # macOS (with Homebrew)
   brew install openssl
   ```

2. **Python 3** (optional, for automatic certificate modification):
   ```bash
   # Usually pre-installed on Linux/macOS
   python3 --version
   ```

3. **xxd** (hexdump utility, usually pre-installed)

## Quick Setup

### Option 1: Automated Script (Recommended)

Run the provided script that does everything automatically:

```bash
cd /home/brett/dev/zamboni-legacy/Zamboni
./scripts/create_fake_cert.sh
```

This will:
1. Create a Certificate Authority (CA)
2. Create a server certificate signed by the CA (using MD5 signature)
3. Export certificate to DER format
4. Modify the DER file to change algorithmIdentifier (MD5 → RSA_PKCS_KEY)
5. Convert back to PFX format for use in Zamboni

The final certificate will be saved as: `certificates/gosredirector.pfx`

### Option 2: Manual Process

If you prefer to do it manually or the script doesn't work:

#### Step 1: Create Certificate Authority

```bash
mkdir -p certificates
cd certificates

# Generate CA private key
openssl genrsa -out OTG3.key.pem 1024

# Create CA certificate (MD5 signature is important!)
openssl req -new -md5 -x509 -days 28124 \
    -key OTG3.key.pem \
    -out OTG3.crt \
    -subj "/OU=Online Technology Group/O=Electronic Arts, Inc./L=Redwood City/ST=California/C=US/CN=OTG3 Certificate Authority"
```

#### Step 2: Create Server Certificate

```bash
# Generate server private key
openssl genrsa -out gosredirector.key.pem 1024

# Create certificate signing request
openssl req -new -key gosredirector.key.pem \
    -out gosredirector.csr \
    -subj "/CN=gosredirector.ea.com/OU=Global Online Studio/O=Electronic Arts, Inc./ST=California/C=US"

# Create certificate signed by CA (using MD5 - critical!)
openssl x509 -req -in gosredirector.csr \
    -CA OTG3.crt -CAkey OTG3.key.pem -CAcreateserial \
    -out gosredirector.crt \
    -days 10000 -md5
```

#### Step 3: Export to DER Format

```bash
openssl x509 -outform der -in gosredirector.crt -out gosredirector.der
```

#### Step 4: Modify the DER File (The Exploit)

This is the key step - we need to change the `algorithmIdentifier` in the certificate's signature section.

**Find the pattern:** `2a864886f70d010104` (MD5)
**Change to:** `2a864886f70d010101` (RSA_PKCS_KEY)

**Using a hex editor:**

1. Open `gosredirector.der` in a hex editor (e.g., `xxd`, `hexedit`, or GUI tool like `bless`)
2. Search for the hex pattern: `2a 86 48 86 f7 0d 01 01 04`
3. There should be **2 occurrences**:
   - First occurrence: The `signature` field (do NOT modify)
   - **Second occurrence**: The `algorithmIdentifier` in the signature section (MODIFY THIS ONE)
4. Change the **last byte** from `04` to `01` in the **second occurrence only**
5. Save as `gosredirector_mod.der`

**Using command line (automated):**

```bash
# Convert to hex, modify, convert back
python3 << 'EOF'
with open('gosredirector.der', 'rb') as f:
    data = bytearray(f.read())

pattern = bytes([0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x04])
replace = bytes([0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01])

count = 0
index = -1
while True:
    index = data.find(pattern, index + 1)
    if index == -1:
        break
    count += 1
    if count == 2:  # Second occurrence
        data[index:index+len(pattern)] = replace
        print(f"Modified at offset {index:04X}")
        break

if count < 2:
    print(f"ERROR: Only found {count} occurrence(s)")
    exit(1)

with open('gosredirector_mod.der', 'wb') as f:
    f.write(data)
EOF
```

#### Step 5: Convert Modified DER to PFX

```bash
# Convert modified DER back to PEM
openssl x509 -inform der -in gosredirector_mod.der -out gosredirector_mod.crt

# Export as PFX with password "password"
openssl pkcs12 -export \
    -out gosredirector.pfx \
    -inkey gosredirector.key.pem \
    -in gosredirector_mod.crt \
    -passout pass:password
```

## Verification

Verify the certificate was created correctly:

```bash
# Check certificate details
openssl pkcs12 -in certificates/gosredirector.pfx -nokeys -passin pass:password | \
    openssl x509 -noout -subject -issuer -fingerprint
```

You should see:
- **Subject:** `CN=gosredirector.ea.com`
- **Issuer:** `CN=OTG3 Certificate Authority`

## Using the Certificate in Zamboni

Zamboni is already configured to load `certificates/gosredirector.pfx`. Once you've created it:

1. Place the certificate at: `certificates/gosredirector.pfx`
2. Password is: `password` (hardcoded in `Program.cs`)
3. Restart Zamboni server

The server will automatically load the certificate and use it for SSL/TLS connections.

## Testing

After creating the certificate:

1. **Start Zamboni server:**
   ```bash
   cd /home/brett/dev/zamboni-legacy/Zamboni
   dotnet run
   ```

2. **Check logs** - You should see:
   ```
   Loading Bug_OldProtoSSL certificate from certificates/gosredirector.pfx
   ```

3. **Connect with NHL Legacy** - The client should now:
   - Complete SSL handshake
   - **Accept the certificate** (instead of rejecting it)
   - Send Blaze packets (GetServerInstanceRequest, etc.)

4. **Expected success logs:**
   ```
   Authenticated as server for connection(1). Stream type: "SecureNetworkStream"
   Connection(1) received packet #1, Component=0x0001, Command=0x0001...
   ```

## How It Works

The Bug_OldProtoSSL exploit works because:

1. **ProtoSSL verifies certificates** by comparing signature hashes using `memcmp()`
2. If the **hash size is 0**, `memcmp()` compares nothing and always returns 0 (success)
3. By changing `algorithmIdentifier` from MD5/SHA1 to `RSA_PKCS_KEY`, ProtoSSL doesn't recognize the signature type
4. This causes the default case in a switch statement to set `iHashSize = 0`
5. Certificate verification "succeeds" because nothing is compared

**Important:** This only works on older ProtoSSL clients. Modern versions have fixed this bug.

## Troubleshooting

**Certificate not found:**
- Make sure it's in `certificates/gosredirector.pfx`
- Check file permissions: `ls -la certificates/`

**Certificate load error:**
- Verify password is "password"
- Try re-creating the certificate

**Client still rejects certificate:**
- Verify you modified the **second** occurrence of the pattern
- Check that the certificate uses MD5 signature (not SHA256)
- Make sure NHL Legacy uses an old enough ProtoSSL version

**OpenSSL errors:**
- Make sure you're using the `-md5` flag when creating certificates
- Verify OpenSSL version: `openssl version`

## References

- [Bug_OldProtoSSL GitHub Repository](https://github.com/Aim4kill/Bug_OldProtoSSL)
- [EA ProtoSSL Source Code Reference](https://github.com/xebecnan/EAWebkit/blob/master/EAWebKitSupportPackages/DirtySDKEAWebKit/local/core/source/proto/protossl.c)

