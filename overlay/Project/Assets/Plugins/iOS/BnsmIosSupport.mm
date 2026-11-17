#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#include "UnityInterface.h"
#include <fcntl.h>
#include <unistd.h>

typedef void (*BnsmBackgroundSave)(int token);

@interface BnsmSupportOverlay : UIView
@property(nonatomic, copy) void (^geometryChanged)(void);
@end
@implementation BnsmSupportOverlay
- (UIView *)hitTest:(CGPoint)point withEvent:(UIEvent *)event {
    UIView *hit=[super hitTest:point withEvent:event];return hit==self?nil:hit;
}
- (void)layoutSubviews { [super layoutSubviews];if(self.geometryChanged)self.geometryChanged(); }
@end

@interface BnsmSupportButton : UIButton
@property(nonatomic, copy) void (^positionChanged)(void);
@end
@implementation BnsmSupportButton
- (void)safeAreaInsetsDidChange { [super safeAreaInsetsDidChange]; if(self.positionChanged)self.positionChanged(); }
@end

@interface BnsmIosSupport : NSObject <UIDocumentPickerDelegate>
@property(nonatomic, copy) NSString *receiver;
@property(nonatomic, copy) NSString *importTarget;
@property(nonatomic, copy) NSString *state;
@property(nonatomic, strong) BnsmSupportButton *button;
@property(nonatomic, strong) BnsmSupportOverlay *overlay;
@property(nonatomic, strong) NSMutableDictionary<NSNumber *, NSNumber *> *backgrounds;
@property(nonatomic) BnsmBackgroundSave save;
@property(nonatomic) BOOL ready;
@property(nonatomic) BOOL busy;
@property(nonatomic) BOOL dockRight;
@property(nonatomic) BOOL dragging;
@property(nonatomic) BOOL picking;
@property(nonatomic) CGFloat fraction;
@property(nonatomic) int nextToken;
- (void)mount;
- (void)dock;
- (void)menu;
- (void)choose;
- (void)notice:(NSString *)text;
- (void)finishBackground:(int)token;
@end

static BnsmIosSupport *support;
static UIViewController *BnsmPresenter(void) {
    UIViewController *view = UnityGetGLViewController();
    while (view.presentedViewController && !view.presentedViewController.isBeingDismissed) view=view.presentedViewController;
    return view;
}
static NSString *BnsmString(const char *text) { return text ? [NSString stringWithUTF8String:text] : @""; }

@implementation BnsmIosSupport
- (void)send:(NSString *)command { UnitySendMessage(self.receiver.UTF8String, "Command", command.UTF8String); }
- (CGRect)bounds {
    UIView *view = self.button.superview;
    CGRect bounds = UIEdgeInsetsInsetRect(view.bounds, view.safeAreaInsets);
    return CGRectInset(bounds, 5, 5);
}
- (void)dock {
    if(self.dragging || !self.button.superview)return;
    CGRect bounds=[self bounds]; CGFloat size=48;
    CGFloat low=CGRectGetMinY(bounds)+size/2, high=MAX(low,CGRectGetMaxY(bounds)-size/2);
    self.button.center=CGPointMake(self.dockRight?CGRectGetMaxX(bounds)-size/2:CGRectGetMinX(bounds)+size/2,
                                  low+MAX(0,MIN(1,self.fraction))*(high-low));
}
- (void)mount {
    UIView *view=UnityGetGLViewController().view;
    self.button=[BnsmSupportButton buttonWithType:UIButtonTypeCustom];
    self.button.frame=CGRectMake(0,0,48,48);
    self.button.accessibilityLabel=@"存档与日志控制台";
    UIImage *icon=[UIImage imageWithContentsOfFile:[[NSBundle mainBundle] pathForResource:@"BnsmConsoleIcon" ofType:@"png"]];
    if(icon)[self.button setImage:icon forState:UIControlStateNormal];
    else [self.button setImage:[UIImage systemImageNamed:@"terminal.fill"] forState:UIControlStateNormal];
    self.button.imageView.contentMode=UIViewContentModeScaleAspectFit;
    self.button.backgroundColor=[[UIColor blackColor] colorWithAlphaComponent:.3];
    self.button.layer.cornerRadius=24;
    [self.button addTarget:self action:@selector(menu) forControlEvents:UIControlEventTouchUpInside];
    [self.button addGestureRecognizer:[[UIPanGestureRecognizer alloc] initWithTarget:self action:@selector(pan:)]];
    self.overlay=[[BnsmSupportOverlay alloc] initWithFrame:view.bounds];
    self.overlay.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;
    [view addSubview:self.overlay];[self.overlay addSubview:self.button];
    NSUserDefaults *prefs=NSUserDefaults.standardUserDefaults;
    self.dockRight=[prefs objectForKey:@"bnsm.console.right"]?[prefs boolForKey:@"bnsm.console.right"]:YES;
    self.fraction=[prefs objectForKey:@"bnsm.console.y"]?[prefs doubleForKey:@"bnsm.console.y"]:.16;
    __weak BnsmIosSupport *weakSelf=self;
    self.button.positionChanged=^{[weakSelf dock];};
    self.overlay.geometryChanged=^{[weakSelf dock];};
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(dock) name:UIDeviceOrientationDidChangeNotification object:nil];
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(saveBeforeInactive) name:UIApplicationWillResignActiveNotification object:nil];
    [self dock];
}
- (void)pan:(UIPanGestureRecognizer *)gesture {
    UIView *view=self.button.superview;
    self.dragging=gesture.state!=UIGestureRecognizerStateEnded && gesture.state!=UIGestureRecognizerStateCancelled;
    CGPoint delta=[gesture translationInView:view], center=self.button.center;
    CGRect bounds=[self bounds];
    self.button.center=CGPointMake(MAX(CGRectGetMinX(bounds)+24,MIN(CGRectGetMaxX(bounds)-24,center.x+delta.x)),
                                  MAX(CGRectGetMinY(bounds)+24,MIN(CGRectGetMaxY(bounds)-24,center.y+delta.y)));
    [gesture setTranslation:CGPointZero inView:view];
    if(!self.dragging){
        self.dockRight=self.button.center.x>CGRectGetMidX(bounds);
        self.fraction=(self.button.center.y-CGRectGetMinY(bounds)-24)/MAX(1,bounds.size.height-48);
        [NSUserDefaults.standardUserDefaults setBool:self.dockRight forKey:@"bnsm.console.right"];
        [NSUserDefaults.standardUserDefaults setDouble:self.fraction forKey:@"bnsm.console.y"];
        [UIView animateWithDuration:.18 animations:^{[self dock];}];
    }
}
- (void)present:(UIAlertController *)alert {
    UIViewController *presenter=BnsmPresenter();
    if([presenter isKindOfClass:UIAlertController.class] || self.picking)return;
    alert.popoverPresentationController.sourceView=self.button;
    alert.popoverPresentationController.sourceRect=self.button.bounds;
    [presenter presentViewController:alert animated:YES completion:nil];
}
- (void)notice:(NSString *)text {
    NSLog(@"Bnsm iOS: %@",text);
    UIView *view=self.button.superview;
    UILabel *toast=[[UILabel alloc] init]; toast.text=text; toast.numberOfLines=0;
    toast.textAlignment=NSTextAlignmentCenter;toast.textColor=UIColor.whiteColor;
    toast.backgroundColor=[[UIColor blackColor] colorWithAlphaComponent:.85];
    toast.layer.cornerRadius=12;toast.clipsToBounds=YES;
    CGRect bounds=UIEdgeInsetsInsetRect(view.bounds,view.safeAreaInsets);
    CGFloat width=MIN(500,bounds.size.width-32);
    toast.frame=CGRectMake(CGRectGetMidX(bounds)-width/2,CGRectGetMaxY(bounds)-100,width,80);
    toast.autoresizingMask=UIViewAutoresizingFlexibleTopMargin|UIViewAutoresizingFlexibleLeftMargin|UIViewAutoresizingFlexibleRightMargin;
    [view addSubview:toast];
    [UIView animateWithDuration:.25 delay:4 options:0 animations:^{toast.alpha=0;} completion:^(BOOL done){[toast removeFromSuperview];}];
}
- (void)menu {
    NSString *message=[NSString stringWithFormat:@"%@\n切到后台会保存已提交进度；导出不包含战斗中途恢复点。\n移植与维护 · By QingMo",self.state?:@"准备本机服务"];
    UIAlertController *alert=[UIAlertController alertControllerWithTitle:@"战斗吧剑灵 · iOS 控制台" message:message preferredStyle:UIAlertControllerStyleActionSheet];
    for(NSArray<NSString *> *row in @[@[@"保存存档",@"checkpoint"],@[@"调试菜单",@"debug"],@[@"导出存档与日志",@"export"]]){
        UIAlertAction *action=[UIAlertAction actionWithTitle:(!self.ready && [row[1] isEqual:@"export"])?@"导出启动日志":row[0] style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){[self send:row[1]];}];
        action.enabled=!self.busy&&(![row[1] isEqual:@"checkpoint"]||self.ready);[alert addAction:action];
    }
    if(!self.ready){
        UIAlertAction *retry=[UIAlertAction actionWithTitle:@"继续启动" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){[self send:@"retry"];}];retry.enabled=!self.busy;[alert addAction:retry];
        UIAlertAction *importAction=[UIAlertAction actionWithTitle:@"导入存档" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){[self pick];}];importAction.enabled=!self.busy;[alert addAction:importAction];
    }
    [alert addAction:[UIAlertAction actionWithTitle:@"关闭" style:UIAlertActionStyleCancel handler:nil]];[self present:alert];
}
- (void)choose {
    UIAlertController *alert=[UIAlertController alertControllerWithTitle:@"首次使用 iOS 内置服" message:@"新建独立进度，或选择导出的 .bnsm-save.zip。存档与日志总包请先解压，再选择里面的 save.bnsm-save.zip。" preferredStyle:UIAlertControllerStyleAlert];
    [alert addAction:[UIAlertAction actionWithTitle:@"新建存档" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){[self send:@"new"];}]];
    [alert addAction:[UIAlertAction actionWithTitle:@"导入存档" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){[self pick];}]];
    [self present:alert];
}
- (void)pick {
    if(self.ready||self.busy||self.picking)return;
    self.picking=YES;
    UIDocumentPickerViewController *picker=[[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[UTTypeZIP] asCopy:YES];
    picker.allowsMultipleSelection=NO;picker.delegate=self;
    [BnsmPresenter() presentViewController:picker animated:YES completion:nil];
}
- (void)documentPickerWasCancelled:(UIDocumentPickerViewController *)controller { self.picking=NO; }
- (void)documentPicker:(UIDocumentPickerViewController *)controller didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls {
    if(urls.count!=1){self.picking=NO;return;}
    NSURL *url=urls.firstObject;BOOL scoped=[url startAccessingSecurityScopedResource];
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0),^{
        NSError *error=nil;NSNumber *regular=nil,*size=nil;
        [url getResourceValue:&regular forKey:NSURLIsRegularFileKey error:&error];
        [url getResourceValue:&size forKey:NSURLFileSizeKey error:&error];
        NSString *partial=[self.importTarget stringByAppendingString:@".partial"];
        NSFileManager *fm=NSFileManager.defaultManager;
        BOOL valid=!error&&regular.boolValue&&size.longLongValue>0&&size.longLongValue<=128LL*1024*1024;
        if(valid){
            [fm removeItemAtPath:partial error:NULL];
            valid=[fm copyItemAtURL:url toURL:[NSURL fileURLWithPath:partial] error:&error];
            if(valid){int fd=open(partial.fileSystemRepresentation,O_RDWR);valid=fd>=0&&fsync(fd)==0;if(fd>=0)close(fd);}
            if(valid){[fm removeItemAtPath:self.importTarget error:NULL];valid=[fm moveItemAtPath:partial toPath:self.importTarget error:&error];}
        }
        if(!valid)[fm removeItemAtPath:partial error:NULL];
        if(scoped)[url stopAccessingSecurityScopedResource];
        dispatch_async(dispatch_get_main_queue(),^{
            self.picking=NO;
            if(valid)[self send:@"import"];
            else [self notice:[@"存档读取失败：" stringByAppendingString:error.localizedDescription?:@"请选择不超过 128 MB 的 ZIP 存档文件"]];
        });
    });
}
- (void)saveBeforeInactive {
    if(!self.save)return;
    int token=++self.nextToken;
    UIBackgroundTaskIdentifier task=[UIApplication.sharedApplication beginBackgroundTaskWithName:@"Bnsm committed save" expirationHandler:^{
        NSLog(@"Bnsm checkpoint background time expired");[self finishBackground:token];
    }];
    if(task!=UIBackgroundTaskInvalid)self.backgrounds[@(token)]=@(task);
    self.save(token);
}
- (void)finishBackground:(int)token {
    NSNumber *task=self.backgrounds[@(token)];
    if(task){[self.backgrounds removeObjectForKey:@(token)];[UIApplication.sharedApplication endBackgroundTask:task.unsignedIntegerValue];}
}
@end

extern "C" {
void BnsmIosInstall(const char *receiver,const char *importTarget,BnsmBackgroundSave callback){
    if(support)return;
    support=[BnsmIosSupport new];support.receiver=BnsmString(receiver);support.importTarget=BnsmString(importTarget);
    support.backgrounds=[NSMutableDictionary dictionary];support.save=callback;[support mount];
}
void BnsmIosChooseProfile(){dispatch_async(dispatch_get_main_queue(),^{[support choose];});}
void BnsmIosNotice(const char *text){NSString *copy=BnsmString(text);dispatch_async(dispatch_get_main_queue(),^{[support notice:copy];});}
void BnsmIosState(const char *text,bool ready,bool busy){support.state=BnsmString(text);support.ready=ready;support.busy=busy;}
void BnsmIosFinishBackground(int token){dispatch_async(dispatch_get_main_queue(),^{[support finishBackground:token];});}
void BnsmIosShare(const char *path){
    NSString *copy=BnsmString(path);
    dispatch_async(dispatch_get_main_queue(),^{
        NSString *home=[NSHomeDirectory().stringByResolvingSymlinksInPath stringByAppendingString:@"/"];
        if(![copy.stringByResolvingSymlinksInPath hasPrefix:home]){[support notice:@"分享路径无效"];return;}
        UIActivityViewController *share=[[UIActivityViewController alloc] initWithActivityItems:@[[NSURL fileURLWithPath:copy]] applicationActivities:nil];
        share.popoverPresentationController.sourceView=support.button;share.popoverPresentationController.sourceRect=support.button.bounds;
        [BnsmPresenter() presentViewController:share animated:YES completion:nil];
    });
}
}
