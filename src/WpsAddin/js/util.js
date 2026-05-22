// WPS 新旧版本的枚举值兼容。任务窗格停靠位置会用到这些值。
var WPS_Enum = {
  msoCTPDockPositionLeft: 0,
  msoCTPDockPositionRight: 2
};

function GetUrlPath() {
  let url = decodeURI(document.location.toString());
  if (url.includes("#")) {
    url = url.substring(0, url.indexOf("#"));
  }
  if (url.includes("?")) {
    url = url.substring(0, url.indexOf("?"));
  }
  return url.substring(0, url.lastIndexOf("/"));
}
