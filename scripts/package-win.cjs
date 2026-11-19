const {build,Platform,Arch}=require('electron-builder');
const path=require('node:path');
const {spawnSync}=require('node:child_process');
const version=require('../package.json').version;
const output=process.env.CHATAIBOX_PACKAGE_DIR?path.resolve(process.env.CHATAIBOX_PACKAGE_DIR):path.resolve(__dirname,`../release/${version}`);
build({targets:Platform.WINDOWS.createTarget(['dir'],Arch.x64),config:{directories:{output},electronDist:path.resolve(__dirname,'../node_modules/electron/dist'),win:{signAndEditExecutable:false}}}).then(()=>{
 const archive=path.join(output,`ChatAIBox-${version}-Windows-x64.zip`);
 const result=spawnSync('tar.exe',['-a','-c','-f',archive,'-C',path.join(output,'win-unpacked'),'.'],{stdio:'inherit'});
 if(result.error)throw result.error;if(result.status!==0)throw Error('Windows 压缩包创建失败');console.log('已生成 Windows 压缩包');
}).catch(e=>{console.error(e.message);process.exitCode=1;});
